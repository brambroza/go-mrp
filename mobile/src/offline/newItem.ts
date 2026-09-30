import type { SaveStockDocument } from "@/api/inventory";

import type { QueueDisplay, QueueItem, QueueKind, QueueScope } from "./types";

/** Input of {@link newQueueItem}. */
export interface NewQueueItemInput {
  /** Client-generated id. */
  id: string;
  scope: QueueScope;
  kind: QueueKind;
  payload: SaveStockDocument;
  display: QueueDisplay;
  /** ISO timestamp (UTC). */
  nowIso: string;
  /** Draft that already exists on the server (count sheet); its content still has to be updated. */
  existingDraft?: { id: string; documentNo: string };
}

/** Builds a pending queue item. */
export function newQueueItem(input: NewQueueItemInput): QueueItem {
  return {
    id: input.id,
    tenantId: input.scope.tenantId,
    userId: input.scope.userId,
    kind: input.kind,
    status: "pending",
    payload: input.payload,
    display: input.display,
    serverDocumentId: input.existingDraft?.id ?? null,
    serverDocumentNo: input.existingDraft?.documentNo ?? null,
    serverStatus: null,
    createAttempted: input.existingDraft !== undefined,
    draftSynced: false,
    attempts: 0,
    lastErrorCode: null,
    lastErrorMessage: null,
    lastErrorStatus: null,
    createdAt: input.nowIso,
    updatedAt: input.nowIso,
    sentAt: null,
  };
}
