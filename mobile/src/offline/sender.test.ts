import { createNodeSqliteDatabase } from "@/testing/nodeSqlite";

import { remarkTag, withoutRemarkTag, withRemarkTag } from "./sender";
import { createSqliteQueueStore, migrateQueue } from "./sqliteStore";
import { QueueSync } from "./sync";
import { FakeDocumentApi, httpError, networkError, sampleItem, samplePayload, SCOPE_A, SCOPE_B, timeoutError } from "./testSupport";
import type { QueueStore } from "./types";

let db: ReturnType<typeof createNodeSqliteDatabase>;
let store: QueueStore;
let api: FakeDocumentApi;
let sync: QueueSync;

beforeEach(async () => {
  db = createNodeSqliteDatabase();
  await migrateQueue(db);
  store = createSqliteQueueStore(db);
  api = new FakeDocumentApi();
  sync = new QueueSync({ store, api, now: () => Date.UTC(2026, 8, 29, 5, 0, 0) });
});

afterEach(() => db.close());

describe("sending a document", () => {
  it("creates the draft, stores its id and posts it", async () => {
    const item = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, item);

    const result = await sync.sendNow(SCOPE_A, item.id);

    expect(result.error).toBeUndefined();
    expect(result.item).toMatchObject({ status: "sent", serverStatus: "Posted", serverDocumentNo: "GI-2609-0001" });
    expect(api.calls).toEqual(["create", "post"]);
    expect(api.totalPosts).toBe(1);
    expect(await store.get(SCOPE_A, item.id)).toMatchObject({ status: "sent" });
  });

  it("reports 'waiting for approval' when the tenant requires approval", async () => {
    api.requireApproval = true;
    const item = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, item);
    const result = await sync.sendNow(SCOPE_A, item.id);
    expect(result.item).toMatchObject({ status: "sent", serverStatus: "Submitted" });
  });

  it("writes the queue id into the remark of the draft", async () => {
    const item = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, item);
    await sync.sendNow(SCOPE_A, item.id);
    const [document] = [...api.documents.values()];
    expect(document?.payload.remark).toBe(`line 3 ${remarkTag(item.id)}`);
    expect(withoutRemarkTag(document?.payload.remark)).toBe("line 3");
  });

  it("keeps the remark within 500 characters", () => {
    const tagged = withRemarkTag(samplePayload({ remark: "x".repeat(500) }), "00000000-0000-4000-8000-000000000001");
    expect(tagged.remark).toHaveLength(500);
    expect(tagged.remark?.endsWith(remarkTag("00000000-0000-4000-8000-000000000001"))).toBe(true);
    expect(withRemarkTag(tagged, "00000000-0000-4000-8000-000000000001").remark).toBe(tagged.remark);
    expect(withRemarkTag(samplePayload({ remark: null }), "00000000-0000-4000-8000-000000000001").remark).toBe(remarkTag("00000000-0000-4000-8000-000000000001"));
  });
});

describe("business-rule rejections are not retried", () => {
  it("marks the item failed with the code and message of the API", async () => {
    api.failBefore.post = [httpError(422, "inventory.insufficient_stock", "Not enough stock.")];
    const item = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, item);

    const result = await sync.sendNow(SCOPE_A, item.id);

    expect(result.item).toMatchObject({
      status: "failed",
      lastErrorCode: "inventory.insufficient_stock",
      lastErrorMessage: "Not enough stock.",
      lastErrorStatus: 422,
    });
    expect(result.item?.serverDocumentId).not.toBeNull();
  });

  it("never picks a failed item up again automatically", async () => {
    api.failBefore.create = [httpError(400, "inventory.line.invalid_quantity")];
    const item = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, item);
    await sync.run(SCOPE_A);
    const callsAfterFirstRun = api.calls.length;

    for (let run = 0; run < 5; run += 1) {
      expect(await sync.run(SCOPE_A)).toEqual({ sent: 0, failed: 0, waiting: 0 });
    }
    expect(await sync.sendNow(SCOPE_A, item.id)).toMatchObject({ item: { status: "failed" } });

    expect(api.calls.length).toBe(callsAfterFirstRun);
    expect(api.calls).toEqual(["create"]);
    expect(await store.get(SCOPE_A, item.id)).toMatchObject({ status: "failed", attempts: 1 });
  });

  it("continues with the next item after a rejection", async () => {
    api.failBefore.create = [httpError(400, "inventory.line.invalid_quantity")];
    const first = sampleItem(SCOPE_A);
    const second = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, first);
    await store.insert(SCOPE_A, second);

    expect(await sync.run(SCOPE_A)).toEqual({ sent: 1, failed: 1, waiting: 0 });
    expect((await store.get(SCOPE_A, second.id))?.status).toBe("sent");
  });

  it("re-posts the same draft after the user edits a failed item", async () => {
    api.failBefore.post = [httpError(422, "inventory.insufficient_stock")];
    const item = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, item);
    const failed = (await sync.sendNow(SCOPE_A, item.id)).item;
    if (!failed) {
      throw new Error("item missing");
    }

    const payload = samplePayload({ lines: [{ itemId: "44444444-4444-4444-8444-444444444444", quantity: 1 }] });
    await store.update(SCOPE_A, { ...failed, status: "pending", payload, draftSynced: false, attempts: 0 });
    const result = await sync.sendNow(SCOPE_A, item.id);

    expect(result.item).toMatchObject({ status: "sent", serverDocumentId: failed.serverDocumentId });
    expect(api.calls).toEqual(["create", "post", "update", "post"]);
    expect(api.documents.size).toBe(1);
    expect([...api.documents.values()][0]?.payload.lines[0]?.quantity).toBe(1);
  });
});

describe("connectivity failures", () => {
  it("keeps the item pending and stops the run", async () => {
    api.failBefore.create = [networkError()];
    const first = sampleItem(SCOPE_A);
    const second = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, first);
    await store.insert(SCOPE_A, second);

    const summary = await sync.run(SCOPE_A);

    expect(summary).toMatchObject({ sent: 0, failed: 0, waiting: 2 });
    expect(summary.stoppedBy?.kind).toBe("network");
    expect(api.calls).toEqual(["create"]);
    expect((await store.list(SCOPE_A)).map((item) => item.status)).toEqual(["pending", "pending"]);
  });

  it("sends everything on the next run after reconnecting", async () => {
    api.failBefore.create = [networkError()];
    await store.insert(SCOPE_A, sampleItem(SCOPE_A));
    await store.insert(SCOPE_A, sampleItem(SCOPE_A));
    await sync.run(SCOPE_A);

    expect(await sync.run(SCOPE_A)).toEqual({ sent: 2, failed: 0, waiting: 0 });
    expect(api.totalPosts).toBe(2);
    expect(api.documents.size).toBe(2);
  });

  it("stops the run when the session expired and keeps items pending", async () => {
    api.failBefore.create = [httpError(401)];
    const item = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, item);
    const summary = await sync.run(SCOPE_A);
    expect(summary.stoppedBy?.kind).toBe("auth");
    expect((await store.get(SCOPE_A, item.id))?.status).toBe("pending");
  });
});

describe("duplicate protection", () => {
  it("re-posts the same draft when the response of post was lost", async () => {
    api.failAfter.post = [timeoutError()];
    const item = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, item);

    const first = await sync.sendNow(SCOPE_A, item.id);
    expect(first.item).toMatchObject({ status: "pending", lastErrorCode: "app.timeout" });
    expect(first.item?.serverDocumentId).not.toBeNull();
    expect(api.totalPosts).toBe(1);

    // The retry posts the same document, gets 409, reads it and sees that it is posted.
    const second = await sync.sendNow(SCOPE_A, item.id);
    expect(second.item).toMatchObject({ status: "sent", serverStatus: "Posted", serverDocumentId: first.item?.serverDocumentId });
    expect(api.calls).toEqual(["create", "post", "post", "get"]);
    expect(api.documents.size).toBe(1);
    expect(api.totalPosts).toBe(1);
  });

  it("finds the draft again when the response of create was lost", async () => {
    api.failAfter.create = [timeoutError()];
    const item = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, item);

    const first = await sync.sendNow(SCOPE_A, item.id);
    expect(first.item).toMatchObject({ status: "pending", serverDocumentId: null, createAttempted: true });
    expect(api.documents.size).toBe(1);

    const second = await sync.sendNow(SCOPE_A, item.id);
    expect(second.item?.status).toBe("sent");
    expect(api.calls).toEqual(["create", "findDraftByTag", "post"]);
    expect(api.documents.size).toBe(1);
    expect(api.totalPosts).toBe(1);
  });

  it("creates the draft when the earlier create never reached the server", async () => {
    api.failBefore.create = [networkError()];
    const item = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, item);
    await sync.sendNow(SCOPE_A, item.id);

    const second = await sync.sendNow(SCOPE_A, item.id);
    expect(second.item?.status).toBe("sent");
    expect(api.calls).toEqual(["create", "findDraftByTag", "create", "post"]);
    expect(api.documents.size).toBe(1);
  });

  it("creates the draft again when the user may not list documents", async () => {
    api.failBefore.create = [networkError()];
    api.failBefore.findDraftByTag = [httpError(403, "common.forbidden")];
    const item = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, item);
    await sync.sendNow(SCOPE_A, item.id);
    expect((await sync.sendNow(SCOPE_A, item.id)).item?.status).toBe("sent");
    expect(api.documents.size).toBe(1);
  });

  it("recovers a send that was interrupted by closing the app", async () => {
    const created = await api.create(withRemarkTag(samplePayload(), "00000000-0000-4000-8000-0000000000aa"));
    await api.post(created.id);
    api.calls.length = 0;
    const item = sampleItem(SCOPE_A, { id: "00000000-0000-4000-8000-0000000000aa", status: "sending", attempts: 1, createAttempted: true, serverDocumentId: created.id, serverDocumentNo: created.documentNo, draftSynced: true });
    await store.insert(SCOPE_A, item);

    expect(await sync.run(SCOPE_A)).toEqual({ sent: 1, failed: 0, waiting: 0 });
    expect(api.calls).toEqual(["post", "get"]);
    expect(api.totalPosts).toBe(1);
  });

  it("fails when the document on the server was voided or rejected meanwhile", async () => {
    const item = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, item);
    api.failBefore.post = [networkError()];
    const first = await sync.sendNow(SCOPE_A, item.id);
    const document = api.documents.get(first.item?.serverDocumentId ?? "");
    if (!document) {
      throw new Error("document missing");
    }
    document.status = "Voided";

    const second = await sync.sendNow(SCOPE_A, item.id);
    expect(second.item).toMatchObject({ status: "failed", lastErrorCode: "inventory.document.cannot_post", lastErrorStatus: 409 });
  });

  it("never runs two sends at the same time", async () => {
    const item = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, item);

    const results = await Promise.all([sync.sendNow(SCOPE_A, item.id), sync.run(SCOPE_A), sync.sendNow(SCOPE_A, item.id), sync.run(SCOPE_A)]);

    expect(results[0]).toMatchObject({ item: { status: "sent" } });
    expect(api.calls).toEqual(["create", "post"]);
    expect(api.totalPosts).toBe(1);
  });

  it("updates an existing count sheet instead of creating a document", async () => {
    const sheet = await api.create(samplePayload({ documentType: "Count" }));
    api.calls.length = 0;
    const item = sampleItem(SCOPE_A, { serverDocumentId: sheet.id, serverDocumentNo: sheet.documentNo, createAttempted: true, draftSynced: false, payload: samplePayload({ documentType: "Count" }) }, "count");
    await store.insert(SCOPE_A, item);

    expect((await sync.sendNow(SCOPE_A, item.id)).item?.status).toBe("sent");
    expect(api.calls).toEqual(["update", "post"]);
    expect(api.documents.size).toBe(1);
  });
});

describe("scope of a sync", () => {
  it("sends only the items of the signed-in user", async () => {
    const mine = sampleItem(SCOPE_A);
    const previousUser = sampleItem(SCOPE_B);
    await store.insert(SCOPE_A, mine);
    await store.insert(SCOPE_B, previousUser);

    expect(await sync.run(SCOPE_A)).toEqual({ sent: 1, failed: 0, waiting: 0 });

    expect(api.documents.size).toBe(1);
    expect((await store.get(SCOPE_B, previousUser.id))?.status).toBe("pending");
    expect((await sync.sendNow(SCOPE_A, previousUser.id)).item).toBeNull();
    expect(api.documents.size).toBe(1);
  });

  it("removes sent items after the retention period only", async () => {
    const old = sampleItem(SCOPE_A, { status: "sent", sentAt: "2026-09-20T00:00:00.000Z" });
    const recent = sampleItem(SCOPE_A, { status: "sent", sentAt: "2026-09-28T00:00:00.000Z" });
    await store.insert(SCOPE_A, old);
    await store.insert(SCOPE_A, recent);
    await sync.run(SCOPE_A);
    expect((await store.list(SCOPE_A)).map((item) => item.id)).toEqual([recent.id]);
  });
});
