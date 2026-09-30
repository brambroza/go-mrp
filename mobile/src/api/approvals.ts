import type { Schemas } from "@mrp/api-client";

import { getApi } from "./client";
import { unwrap } from "./http";

/** Approval request with its history. */
export type ApprovalRequest = Schemas["ApprovalRequestDto"];

/** One page of approval requests. */
export type ApprovalPage = Schemas["PagedResultOfApprovalRequestDto"];

/** Requests waiting for the current user. */
export function fetchApprovalInbox(page = 1, pageSize = 50): Promise<ApprovalPage> {
  return unwrap(getApi().GET("/api/v1/approvals/inbox", { params: { query: { page, pageSize } } }));
}

/** One request with its actions. */
export function fetchApproval(id: string): Promise<ApprovalRequest> {
  return unwrap(getApi().GET("/api/v1/approvals/{id}", { params: { path: { id } } }));
}

/** Approves the current step. */
export function approveRequest(id: string, comment: string | null): Promise<ApprovalRequest> {
  return unwrap(getApi().POST("/api/v1/approvals/{id}/approve", { params: { path: { id } }, body: { comment } }));
}

/** Rejects the request; the API requires a comment. */
export function rejectRequest(id: string, comment: string): Promise<ApprovalRequest> {
  return unwrap(getApi().POST("/api/v1/approvals/{id}/reject", { params: { path: { id } }, body: { comment } }));
}
