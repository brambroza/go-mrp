import { AppError } from "@/lib/errors";

import { applyEvent, canApply, InvalidQueueTransition, MAX_AUTO_ATTEMPTS, statusAfterFailure, stopsSync } from "./machine";
import { httpError, networkError, sampleItem, samplePayload, SCOPE_A, timeoutError } from "./testSupport";

const NOW = "2026-09-29T04:00:00.000Z";

describe("queue state machine", () => {
  it("moves pending → sending → sent", () => {
    const pending = sampleItem(SCOPE_A);
    const sending = applyEvent(pending, { type: "start" }, NOW);
    expect(sending).toMatchObject({ status: "sending", attempts: 1, updatedAt: NOW });

    const sent = applyEvent(sending, { type: "succeeded", documentId: "doc-1", documentNo: "GI-0001", serverStatus: "Posted" }, NOW);
    expect(sent).toMatchObject({ status: "sent", serverDocumentId: "doc-1", serverDocumentNo: "GI-0001", serverStatus: "Posted", sentAt: NOW, lastErrorCode: null });
  });

  it("keeps 'waiting for approval' as the server status", () => {
    const sending = applyEvent(sampleItem(SCOPE_A), { type: "start" }, NOW);
    const sent = applyEvent(sending, { type: "succeeded", documentId: "doc-1", documentNo: "GI-0001", serverStatus: "Submitted" }, NOW);
    expect(sent.status).toBe("sent");
    expect(sent.serverStatus).toBe("Submitted");
  });

  it("does not change the input item", () => {
    const pending = sampleItem(SCOPE_A);
    const copy = structuredClone(pending);
    applyEvent(pending, { type: "start" }, NOW);
    expect(pending).toEqual(copy);
  });

  it("rejects events that are not allowed in the current status", () => {
    const pending = sampleItem(SCOPE_A);
    const sending = applyEvent(pending, { type: "start" }, NOW);
    const sent = applyEvent(sending, { type: "succeeded", documentId: "d", documentNo: "n", serverStatus: "Posted" }, NOW);

    expect(() => applyEvent(pending, { type: "succeeded", documentId: "d", documentNo: "n", serverStatus: "Posted" }, NOW)).toThrow(InvalidQueueTransition);
    expect(() => applyEvent(pending, { type: "retry" }, NOW)).toThrow(InvalidQueueTransition);
    expect(() => applyEvent(sending, { type: "start" }, NOW)).toThrow(InvalidQueueTransition);
    expect(() => applyEvent(sending, { type: "edited", payload: samplePayload(), display: pending.display }, NOW)).toThrow(InvalidQueueTransition);
    for (const type of ["start", "retry", "interrupted"] as const) {
      expect(() => applyEvent(sent, { type }, NOW)).toThrow(InvalidQueueTransition);
    }
    expect(canApply("sent", "start")).toBe(false);
    expect(canApply("failed", "start")).toBe(false);
  });
});

describe("no automatic retry on 4xx", () => {
  it.each([
    [400, "inventory.line.invalid_quantity"],
    [403, "common.forbidden"],
    [404, "common.not_found"],
    [409, "inventory.document.cannot_post"],
    [422, "inventory.insufficient_stock"],
  ])("marks HTTP %i as failed on the first attempt", (status, code) => {
    const error = httpError(status, code, "Rejected by a business rule.");
    expect(statusAfterFailure(error, 1)).toBe("failed");
    expect(stopsSync(error)).toBe(false);

    const sending = applyEvent(sampleItem(SCOPE_A), { type: "start" }, NOW);
    const failed = applyEvent(sending, { type: "failed", error }, NOW);
    expect(failed).toMatchObject({ status: "failed", lastErrorCode: code, lastErrorStatus: status, lastErrorMessage: "Rejected by a business rule." });
    expect(canApply(failed.status, "start")).toBe(false);
  });

  it("keeps connectivity failures pending without a limit", () => {
    expect(statusAfterFailure(networkError(), 1)).toBe("pending");
    expect(statusAfterFailure(timeoutError(), 500)).toBe("pending");
    expect(stopsSync(networkError())).toBe(true);
    expect(stopsSync(timeoutError())).toBe(true);

    const sending = applyEvent(sampleItem(SCOPE_A), { type: "start" }, NOW);
    expect(applyEvent(sending, { type: "failed", error: networkError() }, NOW)).toMatchObject({ status: "pending", lastErrorCode: "app.network" });
  });

  it("keeps the item pending when the session expired", () => {
    const error = httpError(401);
    expect(error.kind).toBe("auth");
    expect(statusAfterFailure(error, 1)).toBe("pending");
    expect(stopsSync(error)).toBe(true);
  });

  it("retries temporary server errors only up to the limit", () => {
    for (const status of [500, 502, 503, 504, 408, 429]) {
      expect(statusAfterFailure(httpError(status), 1)).toBe("pending");
      expect(statusAfterFailure(httpError(status), MAX_AUTO_ATTEMPTS - 1)).toBe("pending");
      expect(statusAfterFailure(httpError(status), MAX_AUTO_ATTEMPTS)).toBe("failed");
    }
    expect(stopsSync(httpError(503))).toBe(false);
  });

  it("treats an unknown error without status as failed", () => {
    expect(statusAfterFailure(new AppError({ kind: "http", code: "app.queue.missing_document" }), 1)).toBe("failed");
  });

  it("limits the stored error text", () => {
    const sending = applyEvent(sampleItem(SCOPE_A), { type: "start" }, NOW);
    const failed = applyEvent(sending, { type: "failed", error: httpError(400, "x", "y".repeat(1000)) }, NOW);
    expect(failed.lastErrorMessage).toHaveLength(300);
  });
});

describe("manual actions", () => {
  it("puts a failed item back to pending on manual retry", () => {
    const failed = sampleItem(SCOPE_A, { status: "failed", attempts: 5, lastErrorCode: "inventory.insufficient_stock", serverDocumentId: "doc-1", draftSynced: true });
    const retried = applyEvent(failed, { type: "retry" }, NOW);
    expect(retried).toMatchObject({ status: "pending", attempts: 0, serverDocumentId: "doc-1", draftSynced: true });
  });

  it("marks the draft as out of date after an edit and keeps the server id", () => {
    const failed = sampleItem(SCOPE_A, { status: "failed", serverDocumentId: "doc-1", draftSynced: true, lastErrorCode: "inventory.insufficient_stock" });
    const payload = samplePayload({ lines: [{ itemId: "44444444-4444-4444-8444-444444444444", quantity: 1 }] });
    const edited = applyEvent(failed, { type: "edited", payload, display: failed.display }, NOW);
    expect(edited).toMatchObject({ status: "pending", serverDocumentId: "doc-1", draftSynced: false, lastErrorCode: null, payload });
  });

  it("recovers an interrupted send without losing the server id", () => {
    const sending = sampleItem(SCOPE_A, { status: "sending", attempts: 1, serverDocumentId: "doc-1", createAttempted: true });
    expect(applyEvent(sending, { type: "interrupted" }, NOW)).toMatchObject({ status: "pending", attempts: 1, serverDocumentId: "doc-1", createAttempted: true });
  });
});
