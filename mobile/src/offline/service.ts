import { createDocument, fetchDocument, findDraftByRemarkTag, postDocument, updateDocument, type SaveStockDocument } from "@/api/inventory";
import { AppError } from "@/lib/errors";
import { newId } from "@/lib/uuid";
import { documentSchema, firstIssue } from "@/validation/schemas";

import { applyEvent } from "./machine";
import { newQueueItem } from "./newItem";
import type { DocumentApi } from "./sender";
import { createSqliteQueueStore, migrateQueue, type SqlDatabase } from "./sqliteStore";
import { QueueSync } from "./sync";
import type { QueueDisplay, QueueItem, QueueKind, QueueScope, QueueStore } from "./types";

/** File name of the queue database on the device. */
const DATABASE_NAME = "mrp-offline-queue.db";

let storePromise: Promise<QueueStore> | null = null;
let sync: QueueSync | null = null;
const changeListeners = new Set<() => void>();

/** Opens the queue database once and returns its store. */
export function getQueueStore(): Promise<QueueStore> {
  storePromise ??= (async () => {
    const SQLite = await import("expo-sqlite");
    const db = (await SQLite.openDatabaseAsync(DATABASE_NAME)) as unknown as SqlDatabase;
    await migrateQueue(db);
    return createSqliteQueueStore(db);
  })().catch((error: unknown) => {
    storePromise = null;
    throw error;
  });
  return storePromise;
}

/** API calls of the sender, backed by the typed client. */
const documentApi: DocumentApi = {
  create: async (payload) => toRef(await createDocument(payload)),
  update: async (id, payload) => toRef(await updateDocument(id, payload)),
  post: async (id) => toRef(await postDocument(id)),
  get: async (id) => toRef(await fetchDocument(id)),
  findDraftByTag: (type, documentDate, tag) => findDraftByRemarkTag(type, documentDate, tag),
};

/** Keeps the fields of a document the queue needs. */
function toRef(document: { id: string; documentNo: string; status: QueueItem["serverStatus"] }) {
  return { id: document.id, documentNo: document.documentNo, status: document.status ?? "Draft" };
}

/** Registers a function called whenever the queue changes. Returns the function that unregisters. */
export function onQueueChanged(listener: () => void): () => void {
  changeListeners.add(listener);
  return () => changeListeners.delete(listener);
}

/** Tells listeners that the queue changed. */
function notifyChanged(): void {
  changeListeners.forEach((listener) => listener());
}

/** The sync engine of the app. */
export async function getQueueSync(): Promise<QueueSync> {
  if (!sync) {
    const store = await getQueueStore();
    sync ??= new QueueSync({ store, api: documentApi, onChange: notifyChanged });
  }
  return sync;
}

/** Input of {@link submitDocument}. */
export interface SubmitInput {
  scope: QueueScope;
  kind: QueueKind;
  payload: SaveStockDocument;
  display: QueueDisplay;
  /** Whether the device is online; offline documents go straight to the queue. */
  online: boolean;
  /** Draft that already exists on the server (count sheet). */
  existingDraft?: { id: string; documentNo: string };
  /** Queue item of an earlier attempt from the same form; it is replaced instead of duplicated. */
  queueItemId?: string | null;
}

/** Result of {@link submitDocument}. */
export type SubmitOutcome =
  /** The API accepted the document: posted, or waiting for approval. */
  | { outcome: "sent"; item: QueueItem }
  /** Saved on the device; it is sent when the connection returns. */
  | { outcome: "queued"; item: QueueItem }
  /** The API rejected the document; the item stays in the queue as failed. */
  | { outcome: "failed"; item: QueueItem; error: AppError };

/**
 * Validates a document, saves it to the queue and, when online, sends it right away.
 * Every document goes through the queue, so that a retry always continues with the same draft.
 */
export async function submitDocument(input: SubmitInput): Promise<SubmitOutcome> {
  const parsed = documentSchema.safeParse(input.payload);
  if (!parsed.success) {
    const issue = firstIssue(parsed.error);
    throw new AppError({ kind: "http", status: 400, code: issue.message, detail: issue.path });
  }

  const store = await getQueueStore();
  const nowIso = new Date().toISOString();
  const existing = input.queueItemId ? await store.get(input.scope, input.queueItemId) : null;

  let item: QueueItem;
  if (existing && (existing.status === "failed" || existing.status === "pending")) {
    item = applyEvent(existing, { type: "edited", payload: input.payload, display: input.display }, nowIso);
    await store.update(input.scope, item);
  } else {
    item = newQueueItem({ id: newId(), scope: input.scope, kind: input.kind, payload: input.payload, display: input.display, nowIso, existingDraft: input.existingDraft });
    await store.insert(input.scope, item);
  }
  notifyChanged();

  if (!input.online) {
    return { outcome: "queued", item };
  }
  const engine = await getQueueSync();
  const result = await engine.sendNow(input.scope, item.id);
  const sent = result.item ?? item;
  if (sent.status === "sent") {
    return { outcome: "sent", item: sent };
  }
  if (sent.status === "failed" && result.error) {
    return { outcome: "failed", item: sent, error: result.error };
  }
  return { outcome: "queued", item: sent };
}

/** Puts a failed item back in line and sends it when online ("ส่งอีกครั้ง"). */
export async function retryItem(scope: QueueScope, id: string, online: boolean): Promise<QueueItem | null> {
  const store = await getQueueStore();
  const item = await store.get(scope, id);
  if (!item) {
    return null;
  }
  let next = item;
  if (item.status === "failed") {
    next = applyEvent(item, { type: "retry" }, new Date().toISOString());
    await store.update(scope, next);
    notifyChanged();
  }
  if (!online || next.status !== "pending") {
    return next;
  }
  const engine = await getQueueSync();
  return (await engine.sendNow(scope, id)).item;
}

/** Saves the edited payload of a pending or failed item; the next send updates the draft first. */
export async function editItem(scope: QueueScope, id: string, payload: SaveStockDocument, display: QueueDisplay): Promise<QueueItem | null> {
  const parsed = documentSchema.safeParse(payload);
  if (!parsed.success) {
    const issue = firstIssue(parsed.error);
    throw new AppError({ kind: "http", status: 400, code: issue.message, detail: issue.path });
  }
  const store = await getQueueStore();
  const item = await store.get(scope, id);
  if (!item || (item.status !== "failed" && item.status !== "pending")) {
    return item;
  }
  const edited = applyEvent(item, { type: "edited", payload, display }, new Date().toISOString());
  await store.update(scope, edited);
  notifyChanged();
  return edited;
}

/** Removes an item from the device. A draft that already exists on the server stays there. */
export async function discardItem(scope: QueueScope, id: string): Promise<void> {
  const engine = await getQueueSync();
  await engine.idle();
  const store = await getQueueStore();
  const item = await store.get(scope, id);
  if (item?.status === "sending") {
    return;
  }
  await store.remove(scope, id);
  notifyChanged();
}

/** Runs a sync for the scope; errors of single items are stored with the items. */
export async function syncQueue(scope: QueueScope) {
  const engine = await getQueueSync();
  return engine.run(scope);
}
