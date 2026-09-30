import type { SaveStockDocument, StockDocumentStatus } from "@/api/inventory";

/** Status of a queue item. */
export type QueueStatus =
  /** Waiting to be sent; picked up by the next sync. */
  | "pending"
  /** A send is running right now. */
  | "sending"
  /** The API rejected the document, or too many temporary failures; needs a decision by the user. */
  | "failed"
  /** The document reached the API and was posted or sent for approval. */
  | "sent";

/** Kind of document in the queue (decides the screen title and icon). */
export type QueueKind = "receipt" | "issue" | "transfer" | "count";

/** Owner of queue items: one tenant and one user. Items of other scopes are invisible. */
export interface QueueScope {
  tenantId: string;
  userId: string;
}

/** Text shown for a line of a queued document (the payload only has ids). */
export interface QueueLineLabel {
  itemCode: string;
  itemName: string;
  unitCode?: string;
  lotNo?: string;
}

/** Display data stored next to the payload. Never contains tokens. */
export interface QueueDisplay {
  /** Short title, e.g. the PO number or the warehouse code. */
  title: string;
  /** One label per payload line, same order. */
  lines: QueueLineLabel[];
}

/** One document waiting on the device. */
export interface QueueItem {
  /** Client-generated id; also written into the remark of the draft to find it again. */
  id: string;
  tenantId: string;
  userId: string;
  kind: QueueKind;
  status: QueueStatus;
  /** Request body of the draft. */
  payload: SaveStockDocument;
  display: QueueDisplay;
  /** Id of the draft on the server, stored as soon as it is known. */
  serverDocumentId: string | null;
  /** Document number given by the server. */
  serverDocumentNo: string | null;
  /** Status of the document on the server after sending (`Posted`, or `Submitted` = waiting for approval). */
  serverStatus: StockDocumentStatus | null;
  /** `true` when a create request was sent and its outcome is unknown or successful. */
  createAttempted: boolean;
  /** `true` when the draft on the server has the same content as the payload. */
  draftSynced: boolean;
  /** Number of send attempts. */
  attempts: number;
  lastErrorCode: string | null;
  lastErrorMessage: string | null;
  lastErrorStatus: number | null;
  /** ISO timestamps (UTC). */
  createdAt: string;
  updatedAt: string;
  sentAt: string | null;
}

/** Storage of the queue. Every method is limited to one scope. */
export interface QueueStore {
  /** Adds an item; its tenant and user must equal the scope. */
  insert(scope: QueueScope, item: QueueItem): Promise<void>;
  /** Replaces an item of the scope; does nothing when the item belongs to another scope. */
  update(scope: QueueScope, item: QueueItem): Promise<void>;
  /** Reads one item of the scope. */
  get(scope: QueueScope, id: string): Promise<QueueItem | null>;
  /** Items of the scope, oldest first. */
  list(scope: QueueScope, statuses?: readonly QueueStatus[]): Promise<QueueItem[]>;
  /** Number of items of the scope with one of the statuses. */
  count(scope: QueueScope, statuses: readonly QueueStatus[]): Promise<number>;
  /** Deletes one item of the scope. */
  remove(scope: QueueScope, id: string): Promise<void>;
  /** Deletes sent items of the scope that were sent before the timestamp. */
  removeSentBefore(scope: QueueScope, isoTimestamp: string): Promise<number>;
  /** Puts items left in `sending` (app was closed mid-send) back to `pending`. */
  recoverInterrupted(scope: QueueScope, nowIso: string): Promise<number>;
}
