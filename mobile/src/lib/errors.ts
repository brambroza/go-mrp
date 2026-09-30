import { asProblem, type ApiProblem } from "@mrp/api-client";

/** Broad class of a failed call; decides whether the offline queue may retry. */
export type AppErrorKind =
  /** No connection, DNS failure, connection reset. The request may or may not have reached the API. */
  | "network"
  /** No response within the time limit. The request may have succeeded on the server. */
  | "timeout"
  /** The session is no longer valid (401 after a refresh attempt). */
  | "auth"
  /** The API answered with an error status. */
  | "http";

/** Error thrown by every API call of the app. Never contains tokens. */
export class AppError extends Error {
  /** Broad class of the failure. */
  readonly kind: AppErrorKind;
  /** HTTP status when the API answered. */
  readonly status: number | undefined;
  /** Stable error code of the API, e.g. `inventory.insufficient_stock`. */
  readonly code: string | undefined;
  /** Human readable detail from the API (English); used when the code has no translation. */
  readonly detail: string | undefined;
  /** Validation errors by field. */
  readonly fieldErrors: Record<string, string[]> | undefined;

  constructor(init: {
    kind: AppErrorKind;
    status?: number;
    code?: string;
    detail?: string;
    fieldErrors?: Record<string, string[]>;
  }) {
    super(init.code ?? init.detail ?? init.kind);
    this.name = "AppError";
    this.kind = init.kind;
    this.status = init.status;
    this.code = init.code;
    this.detail = init.detail;
    this.fieldErrors = init.fieldErrors;
  }
}

/** Builds an {@link AppError} from an error response of the API. */
export function errorFromResponse(status: number, body: unknown): AppError {
  const problem: ApiProblem = asProblem(body);
  return new AppError({
    kind: status === 401 ? "auth" : "http",
    status,
    code: typeof problem.code === "string" ? problem.code : undefined,
    detail: typeof problem.detail === "string" ? problem.detail : typeof problem.title === "string" ? problem.title : undefined,
    fieldErrors: problem.errors && typeof problem.errors === "object" ? problem.errors : undefined,
  });
}

/** Converts anything thrown by `fetch` or the app into an {@link AppError}. */
export function toAppError(error: unknown): AppError {
  if (error instanceof AppError) {
    return error;
  }
  const name = typeof error === "object" && error !== null && "name" in error ? String((error as { name: unknown }).name) : "";
  if (name === "AbortError" || name === "TimeoutError") {
    return new AppError({ kind: "timeout" });
  }
  return new AppError({ kind: "network" });
}

/** Returns whether the request may not have reached the API (safe to retry automatically). */
export function isConnectivityError(error: AppError): boolean {
  return error.kind === "network" || error.kind === "timeout";
}

/** Statuses that are temporary on the server side. */
const TRANSIENT_STATUSES = new Set([408, 425, 429, 500, 502, 503, 504]);

/** Returns whether the API failure is temporary (server trouble), not a business-rule rejection. */
export function isTransientHttpError(error: AppError): boolean {
  return error.kind === "http" && error.status !== undefined && TRANSIENT_STATUSES.has(error.status);
}
