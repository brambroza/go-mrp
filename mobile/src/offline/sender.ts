import type { SaveStockDocument, StockDocumentStatus, StockDocumentType } from "@/api/inventory";
import { AppError, toAppError } from "@/lib/errors";

import { applyEvent } from "./machine";
import type { QueueItem, QueueScope, QueueStore } from "./types";

/** Reference to a document on the server. */
export interface DocumentRef {
  id: string;
  documentNo: string;
  status: StockDocumentStatus;
}

/** API calls the sender needs; injected so that the logic can be tested without a network. */
export interface DocumentApi {
  create(payload: SaveStockDocument): Promise<DocumentRef>;
  update(id: string, payload: SaveStockDocument): Promise<DocumentRef>;
  post(id: string): Promise<DocumentRef>;
  get(id: string): Promise<DocumentRef>;
  findDraftByTag(type: StockDocumentType, documentDate: string, tag: string): Promise<{ id: string; documentNo: string } | null>;
}

/** Statuses that mean "the document left the device successfully". */
const DELIVERED: readonly StockDocumentStatus[] = ["Posted", "Submitted", "Approved"];

/** Longest remark accepted by the API. */
const MAX_REMARK = 500;

/** Tag written into the remark of a draft so that it can be found again after a lost response. */
export function remarkTag(itemId: string): string {
  return `#m:${itemId}`;
}

/** Longest remark the user may type; leaves room for {@link remarkTag}. */
export const MAX_USER_REMARK = MAX_REMARK - 45;

/** Adds the tag of the queue item to the remark of the payload. */
export function withRemarkTag(payload: SaveStockDocument, itemId: string): SaveStockDocument {
  const tag = remarkTag(itemId);
  const remark = (payload.remark ?? "").trim();
  if (remark.includes(tag)) {
    return payload;
  }
  const text = remark === "" ? tag : `${remark.slice(0, MAX_REMARK - tag.length - 1)} ${tag}`;
  return { ...payload, remark: text };
}

/** Removes the tag from a remark before it is shown to the user. */
export function withoutRemarkTag(remark: string | null | undefined): string {
  return (remark ?? "").replace(/\s?#m:[0-9a-f-]{36}/gi, "").trim();
}

/** Dependencies of {@link sendItem}. */
export interface SendContext {
  store: QueueStore;
  api: DocumentApi;
  scope: QueueScope;
  /** Clock returning an ISO timestamp. */
  now: () => string;
}

/** Result of sending one item. */
export interface SendResult {
  item: QueueItem;
  /** Present when the send did not succeed. */
  error?: AppError;
}

/** Returns whether the error says that the document is no longer a draft. */
function isStateConflict(error: AppError): boolean {
  return error.kind === "http" && error.status === 409;
}

/**
 * Sends one queue item in steps that can be repeated safely:
 *
 * 1. find or create the draft, and store its server id before anything else happens;
 * 2. bring the draft up to date when the payload was edited;
 * 3. post the draft. A conflict (409) means that an earlier attempt may have succeeded, so the
 *    document is read again: `Posted`/`Submitted`/`Approved` counts as success.
 *
 * The item must be `pending`. The updated item is persisted after every step.
 */
export async function sendItem(context: SendContext, input: QueueItem): Promise<SendResult> {
  const { store, api, scope, now } = context;
  if (input.tenantId !== scope.tenantId || input.userId !== scope.userId) {
    throw new Error("Queue item belongs to another tenant or user.");
  }

  let item = applyEvent(input, { type: "start" }, now());
  await store.update(scope, item);

  try {
    const payload = withRemarkTag(item.payload, item.id);

    if (!item.serverDocumentId) {
      let draft: { id: string; documentNo: string } | null = null;
      if (item.createAttempted) {
        draft = await findOrphan(api, payload, item.id);
      }
      if (!draft) {
        // Remember that a create request is about to leave, even if the app is closed right after.
        item = { ...item, createAttempted: true, updatedAt: now() };
        await store.update(scope, item);
        draft = await api.create(payload);
      }
      item = { ...item, serverDocumentId: draft.id, serverDocumentNo: draft.documentNo, draftSynced: true, updatedAt: now() };
      await store.update(scope, item);
    }

    const documentId = item.serverDocumentId;
    if (!documentId) {
      throw new AppError({ kind: "http", code: "app.queue.missing_document" });
    }

    if (!item.draftSynced) {
      try {
        await api.update(documentId, payload);
        item = { ...item, draftSynced: true, updatedAt: now() };
        await store.update(scope, item);
      } catch (error) {
        const appError = toAppError(error);
        const delivered = isStateConflict(appError) ? await readDelivered(api, documentId) : null;
        if (!delivered) {
          throw appError;
        }
        return await succeed(context, item, delivered);
      }
    }

    let posted: DocumentRef;
    try {
      posted = await api.post(documentId);
    } catch (error) {
      const appError = toAppError(error);
      const delivered = isStateConflict(appError) ? await readDelivered(api, documentId) : null;
      if (!delivered) {
        throw appError;
      }
      posted = delivered;
    }
    return await succeed(context, item, posted);
  } catch (error) {
    const appError = toAppError(error);
    item = applyEvent(item, { type: "failed", error: appError }, now());
    await store.update(scope, item);
    return { item, error: appError };
  }
}

/** Marks the item as sent and persists it. */
async function succeed(context: SendContext, item: QueueItem, document: DocumentRef): Promise<SendResult> {
  const sent = applyEvent(
    item,
    { type: "succeeded", documentId: document.id, documentNo: document.documentNo, serverStatus: document.status },
    context.now(),
  );
  await context.store.update(context.scope, sent);
  return { item: sent };
}

/** Reads the document and returns it when it already left the draft state successfully. */
async function readDelivered(api: DocumentApi, documentId: string): Promise<DocumentRef | null> {
  const document = await api.get(documentId);
  return DELIVERED.includes(document.status) ? document : null;
}

/**
 * Looks for the draft of an earlier create request whose response was lost. A user without the
 * permission to list documents (403) cannot search; the draft is then created again.
 */
async function findOrphan(api: DocumentApi, payload: SaveStockDocument, itemId: string): Promise<{ id: string; documentNo: string } | null> {
  try {
    return await api.findDraftByTag(payload.documentType, payload.documentDate, remarkTag(itemId));
  } catch (error) {
    const appError = toAppError(error);
    if (appError.kind === "http" && (appError.status === 403 || appError.status === 404)) {
      return null;
    }
    throw appError;
  }
}
