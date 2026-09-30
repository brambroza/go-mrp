import { createNodeSqliteDatabase } from "@/testing/nodeSqlite";

import { createSqliteQueueStore, migrateQueue } from "./sqliteStore";
import { sampleItem, SCOPE_A, SCOPE_B, SCOPE_C } from "./testSupport";
import type { QueueStore } from "./types";

let db: ReturnType<typeof createNodeSqliteDatabase>;
let store: QueueStore;

beforeEach(async () => {
  db = createNodeSqliteDatabase();
  await migrateQueue(db);
  await migrateQueue(db); // running the migration twice is harmless
  store = createSqliteQueueStore(db);
});

afterEach(() => db.close());

describe("tenant and user scoping", () => {
  it("shows a user only their own items", async () => {
    const mine = sampleItem(SCOPE_A);
    const colleague = sampleItem(SCOPE_B);
    const otherTenant = sampleItem(SCOPE_C);
    await store.insert(SCOPE_A, mine);
    await store.insert(SCOPE_B, colleague);
    await store.insert(SCOPE_C, otherTenant);

    expect((await store.list(SCOPE_A)).map((item) => item.id)).toEqual([mine.id]);
    expect((await store.list(SCOPE_B)).map((item) => item.id)).toEqual([colleague.id]);
    expect((await store.list(SCOPE_C)).map((item) => item.id)).toEqual([otherTenant.id]);
    expect(await store.count(SCOPE_A, ["pending"])).toBe(1);
  });

  it("does not return an item of another user by id", async () => {
    const mine = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, mine);
    expect(await store.get(SCOPE_A, mine.id)).toMatchObject({ id: mine.id });
    expect(await store.get(SCOPE_B, mine.id)).toBeNull();
    expect(await store.get(SCOPE_C, mine.id)).toBeNull();
  });

  it("refuses to insert or update an item through a different scope", async () => {
    const mine = sampleItem(SCOPE_A);
    await expect(store.insert(SCOPE_B, mine)).rejects.toThrow("another tenant or user");
    await store.insert(SCOPE_A, mine);
    await expect(store.update(SCOPE_C, { ...mine, status: "sent" })).rejects.toThrow("another tenant or user");
    expect((await store.get(SCOPE_A, mine.id))?.status).toBe("pending");
  });

  it("cannot take over an item by claiming it for another scope", async () => {
    const mine = sampleItem(SCOPE_A);
    await store.insert(SCOPE_A, mine);
    await store.update(SCOPE_B, { ...mine, ...SCOPE_B, status: "failed" });
    expect(await store.get(SCOPE_A, mine.id)).toMatchObject({ status: "pending", userId: SCOPE_A.userId });
    expect(await store.list(SCOPE_B)).toEqual([]);
  });

  it("does not delete or recover items of another scope", async () => {
    const mine = sampleItem(SCOPE_A, { status: "sending" });
    const old = sampleItem(SCOPE_A, { status: "sent", sentAt: "2026-01-01T00:00:00.000Z" });
    await store.insert(SCOPE_A, mine);
    await store.insert(SCOPE_A, old);

    await store.remove(SCOPE_B, mine.id);
    expect(await store.recoverInterrupted(SCOPE_B, "2026-09-29T00:00:00.000Z")).toBe(0);
    expect(await store.removeSentBefore(SCOPE_C, "2026-09-29T00:00:00.000Z")).toBe(0);
    expect(await store.list(SCOPE_A)).toHaveLength(2);
    expect((await store.get(SCOPE_A, mine.id))?.status).toBe("sending");

    expect(await store.recoverInterrupted(SCOPE_A, "2026-09-29T00:00:00.000Z")).toBe(1);
    expect(await store.removeSentBefore(SCOPE_A, "2026-09-29T00:00:00.000Z")).toBe(1);
    expect((await store.list(SCOPE_A)).map((item) => item.status)).toEqual(["pending"]);
  });

  it("requires a complete scope", async () => {
    await expect(store.insert({ tenantId: "", userId: "" }, sampleItem({ tenantId: "", userId: "" }))).rejects.toThrow("requires a tenant and a user");
  });
});

describe("storage", () => {
  it("round-trips every field", async () => {
    const item = sampleItem(SCOPE_A, {
      status: "failed",
      serverDocumentId: "d0000000-0000-4000-8000-000000000001",
      serverDocumentNo: "GI-2609-0001",
      serverStatus: "Draft",
      createAttempted: true,
      draftSynced: true,
      attempts: 3,
      lastErrorCode: "inventory.insufficient_stock",
      lastErrorMessage: "สต็อกไม่พอ ' OR 1=1 --",
      lastErrorStatus: 422,
    });
    await store.insert(SCOPE_A, item);
    expect(await store.get(SCOPE_A, item.id)).toEqual(item);
  });

  it("lists oldest first and filters by status", async () => {
    const first = sampleItem(SCOPE_A, { createdAt: "2026-09-29T01:00:00.000Z" });
    const second = sampleItem(SCOPE_A, { createdAt: "2026-09-29T02:00:00.000Z", status: "failed" });
    const third = sampleItem(SCOPE_A, { createdAt: "2026-09-29T03:00:00.000Z" });
    await store.insert(SCOPE_A, third);
    await store.insert(SCOPE_A, first);
    await store.insert(SCOPE_A, second);

    expect((await store.list(SCOPE_A)).map((item) => item.id)).toEqual([first.id, second.id, third.id]);
    expect((await store.list(SCOPE_A, ["pending"])).map((item) => item.id)).toEqual([first.id, third.id]);
    expect(await store.count(SCOPE_A, ["pending", "failed"])).toBe(3);
    expect(await store.count(SCOPE_A, [])).toBe(0);
  });

  it("stores no token-like columns", async () => {
    const columns = await db.getAllAsync<{ name: string }>("SELECT name FROM pragma_table_info('offline_queue')", []);
    const names = columns.map((column) => column.name);
    expect(names).toContain("tenant_id");
    expect(names).toContain("user_id");
    expect(names.filter((name) => /token|password|secret|authorization/i.test(name))).toEqual([]);
  });
});
