import type { SaveStockDocument } from "@/api/inventory";
import { AppError } from "@/lib/errors";

import { newQueueItem } from "./newItem";
import type { DocumentApi, DocumentRef } from "./sender";
import type { QueueItem, QueueKind, QueueScope } from "./types";

/** Scope of the first test user. */
export const SCOPE_A: QueueScope = { tenantId: "11111111-1111-4111-8111-111111111111", userId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa" };
/** Another user of the same tenant. */
export const SCOPE_B: QueueScope = { tenantId: SCOPE_A.tenantId, userId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb" };
/** A user with the same id in another tenant. */
export const SCOPE_C: QueueScope = { tenantId: "22222222-2222-4222-8222-222222222222", userId: SCOPE_A.userId };

/** Payload of a small issue document. */
export function samplePayload(overrides: Partial<SaveStockDocument> = {}): SaveStockDocument {
  return {
    documentType: "Issue",
    documentDate: "2026-09-29",
    warehouseId: "33333333-3333-4333-8333-333333333333",
    lines: [{ itemId: "44444444-4444-4444-8444-444444444444", quantity: 2.5 }],
    remark: "line 3",
    ...overrides,
  };
}

let counter = 0;

/** Builds a pending item with a unique id. */
export function sampleItem(scope: QueueScope, overrides: Partial<QueueItem> = {}, kind: QueueKind = "issue"): QueueItem {
  counter += 1;
  const id = `00000000-0000-4000-8000-${String(counter).padStart(12, "0")}`;
  const nowIso = new Date(Date.UTC(2026, 8, 29, 3, 0, counter)).toISOString();
  return {
    ...newQueueItem({ id, scope, kind, payload: samplePayload(), display: { title: "WH-01", lines: [{ itemCode: "RM-001", itemName: "Sugar" }] }, nowIso }),
    ...overrides,
  };
}

/** Error like the one thrown when the device is offline. */
export const networkError = () => new AppError({ kind: "network" });
/** Error like the one thrown when no response arrives in time. */
export const timeoutError = () => new AppError({ kind: "timeout" });
/** Error response of the API. */
export const httpError = (status: number, code?: string, detail?: string) => new AppError({ kind: status === 401 ? "auth" : "http", status, code, detail });

/** Document on the fake server. */
interface ServerDocument extends DocumentRef {
  payload: SaveStockDocument;
  postCount: number;
}

/**
 * Fake API with the behaviour of the real one: `post` moves a draft to `Posted` (or `Submitted`
 * when approval is required) and answers 409 for a posted document. Failures are scripted per call.
 */
export class FakeDocumentApi implements DocumentApi {
  readonly documents = new Map<string, ServerDocument>();
  readonly calls: string[] = [];
  requireApproval = false;
  /** Errors thrown by the next calls of a method, before the server does anything. */
  readonly failBefore: Partial<Record<keyof DocumentApi, AppError[]>> = {};
  /** Errors thrown by the next calls of a method after the server did the work (lost response). */
  readonly failAfter: Partial<Record<keyof DocumentApi, AppError[]>> = {};
  private sequence = 0;

  private step(method: keyof DocumentApi): void {
    this.calls.push(method);
    const error = this.failBefore[method]?.shift();
    if (error) {
      throw error;
    }
  }

  private finish(method: keyof DocumentApi): void {
    const error = this.failAfter[method]?.shift();
    if (error) {
      throw error;
    }
  }

  async create(payload: SaveStockDocument): Promise<DocumentRef> {
    this.step("create");
    this.sequence += 1;
    const id = `d0000000-0000-4000-8000-${String(this.sequence).padStart(12, "0")}`;
    const document: ServerDocument = { id, documentNo: `GI-2609-${String(this.sequence).padStart(4, "0")}`, status: "Draft", payload, postCount: 0 };
    this.documents.set(id, document);
    this.finish("create");
    return { id, documentNo: document.documentNo, status: document.status };
  }

  async update(id: string, payload: SaveStockDocument): Promise<DocumentRef> {
    this.step("update");
    const document = this.require(id);
    if (document.status !== "Draft" && document.status !== "Rejected") {
      throw httpError(409, "inventory.document.not_editable");
    }
    document.payload = payload;
    this.finish("update");
    return { id, documentNo: document.documentNo, status: document.status };
  }

  async post(id: string): Promise<DocumentRef> {
    this.step("post");
    const document = this.require(id);
    if (document.status === "Draft") {
      document.status = this.requireApproval ? "Submitted" : "Posted";
      document.postCount += 1;
    } else if (document.status !== "Submitted") {
      throw httpError(409, "inventory.document.cannot_post");
    }
    this.finish("post");
    return { id, documentNo: document.documentNo, status: document.status };
  }

  async get(id: string): Promise<DocumentRef> {
    this.step("get");
    const document = this.require(id);
    return { id, documentNo: document.documentNo, status: document.status };
  }

  async findDraftByTag(type: string, documentDate: string, tag: string): Promise<{ id: string; documentNo: string } | null> {
    this.step("findDraftByTag");
    for (const document of this.documents.values()) {
      const { payload } = document;
      if (document.status === "Draft" && payload.documentType === type && payload.documentDate === documentDate && (payload.remark ?? "").includes(tag)) {
        return { id: document.id, documentNo: document.documentNo };
      }
    }
    return null;
  }

  /** Number of stock postings that happened on the server. */
  get totalPosts(): number {
    return [...this.documents.values()].reduce((sum, document) => sum + document.postCount, 0);
  }

  private require(id: string): ServerDocument {
    const document = this.documents.get(id);
    if (!document) {
      throw httpError(404, "common.not_found");
    }
    return document;
  }
}
