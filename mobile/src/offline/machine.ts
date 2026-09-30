import { AppError, isConnectivityError, isTransientHttpError } from "@/lib/errors";

import type { QueueItem, QueueStatus } from "./types";

/** Temporary failures allowed before an item needs a manual retry. */
export const MAX_AUTO_ATTEMPTS = 5;

/** Longest error text stored with an item. */
const MAX_ERROR_LENGTH = 300;

/** Events of the queue state machine. */
export type QueueEvent =
  | { type: "start" }
  | { type: "succeeded"; documentId: string; documentNo: string; serverStatus: QueueItem["serverStatus"] }
  | { type: "failed"; error: AppError }
  | { type: "retry" }
  | { type: "edited"; payload: QueueItem["payload"]; display: QueueItem["display"] }
  | { type: "interrupted" };

/** Allowed transitions: from → events. */
const TRANSITIONS: Record<QueueStatus, readonly QueueEvent["type"][]> = {
  pending: ["start", "edited"],
  sending: ["succeeded", "failed", "interrupted"],
  failed: ["retry", "edited"],
  sent: [],
};

/** Error raised for an event that is not allowed in the current status. */
export class InvalidQueueTransition extends Error {
  constructor(status: QueueStatus, event: QueueEvent["type"]) {
    super(`Queue event '${event}' is not allowed while the item is '${status}'.`);
    this.name = "InvalidQueueTransition";
  }
}

/** Returns whether the event may fire in the status. */
export function canApply(status: QueueStatus, event: QueueEvent["type"]): boolean {
  return TRANSITIONS[status].includes(event);
}

/**
 * Decides what a failed send means:
 * - connectivity (offline, timeout): back to `pending`, retried on the next sync, without limit;
 * - session expired: back to `pending`, retried after the user signs in again;
 * - temporary server trouble (5xx, 408, 429): `pending` until {@link MAX_AUTO_ATTEMPTS}, then `failed`;
 * - any other 4xx (business rule, validation, permission): `failed`, never retried automatically.
 */
export function statusAfterFailure(error: AppError, attempts: number): "pending" | "failed" {
  if (isConnectivityError(error) || error.kind === "auth") {
    return "pending";
  }
  if (isTransientHttpError(error)) {
    return attempts >= MAX_AUTO_ATTEMPTS ? "failed" : "pending";
  }
  return "failed";
}

/** Returns whether the sync must stop after this failure instead of trying the next item. */
export function stopsSync(error: AppError): boolean {
  return isConnectivityError(error) || error.kind === "auth";
}

/** Applies an event to an item and returns the new item. Pure: the input is not changed. */
export function applyEvent(item: QueueItem, event: QueueEvent, nowIso: string): QueueItem {
  if (!canApply(item.status, event.type)) {
    throw new InvalidQueueTransition(item.status, event.type);
  }
  switch (event.type) {
    case "start":
      return { ...item, status: "sending", attempts: item.attempts + 1, updatedAt: nowIso };
    case "succeeded":
      return {
        ...item,
        status: "sent",
        serverDocumentId: event.documentId,
        serverDocumentNo: event.documentNo,
        serverStatus: event.serverStatus,
        draftSynced: true,
        lastErrorCode: null,
        lastErrorMessage: null,
        lastErrorStatus: null,
        updatedAt: nowIso,
        sentAt: nowIso,
      };
    case "failed":
      return {
        ...item,
        status: statusAfterFailure(event.error, item.attempts),
        lastErrorCode: event.error.code ?? `app.${event.error.kind}`,
        lastErrorMessage: event.error.detail ? event.error.detail.slice(0, MAX_ERROR_LENGTH) : null,
        lastErrorStatus: event.error.status ?? null,
        updatedAt: nowIso,
      };
    case "retry":
      return { ...item, status: "pending", attempts: 0, updatedAt: nowIso };
    case "edited":
      return {
        ...item,
        status: "pending",
        payload: event.payload,
        display: event.display,
        draftSynced: false,
        attempts: 0,
        lastErrorCode: null,
        lastErrorMessage: null,
        lastErrorStatus: null,
        updatedAt: nowIso,
      };
    case "interrupted":
      return { ...item, status: "pending", updatedAt: nowIso };
  }
}
