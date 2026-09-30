import { AppError, errorFromResponse, toAppError } from "@/lib/errors";

/** Default time limit of one request. Warehouse Wi-Fi is slow, but a hung request must end. */
export const REQUEST_TIMEOUT_MS = 20_000;

/** Function with the signature of `fetch`. */
export type FetchLike = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

/** Wraps `fetch` so that a request is aborted after the time limit. */
export function withTimeout(fetchImpl: FetchLike, timeoutMs: number = REQUEST_TIMEOUT_MS): FetchLike {
  return async (input, init) => {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    try {
      return await fetchImpl(input, { ...init, signal: controller.signal });
    } finally {
      clearTimeout(timer);
    }
  };
}

/** A request reduced to plain values, so that it can be sent more than once. */
export interface RequestSnapshot {
  url: string;
  method: string;
  headers: [string, string][];
  /** Body as text; the API only takes JSON. */
  body: string | undefined;
}

/**
 * Reads a request into plain values. `openapi-fetch` hands over a `Request` object, but the
 * `fetch` of Expo / React Native handles `Request` objects with a body differently between
 * versions; a URL with plain options works everywhere and can be sent again after a 401.
 */
export async function snapshotRequest(input: RequestInfo | URL, init?: RequestInit): Promise<RequestSnapshot> {
  const request = typeof input === "string" || input instanceof URL ? null : input;
  const method = (init?.method ?? request?.method ?? "GET").toUpperCase();
  const headers: [string, string][] = [];
  new Headers(init?.headers ?? request?.headers).forEach((value, name) => headers.push([name, value]));

  let body: string | undefined;
  if (method !== "GET" && method !== "HEAD") {
    if (typeof init?.body === "string") {
      body = init.body;
    } else if (request) {
      const text = await request.text();
      body = text === "" ? undefined : text;
    }
  }
  return { url: request ? request.url : String(input), method, headers, body };
}

/** Sends a snapshot, optionally with another bearer token. */
export function sendSnapshot(fetchImpl: FetchLike, snapshot: RequestSnapshot, accessToken?: string): Promise<Response> {
  const headers = snapshot.headers.filter(([name]) => accessToken === undefined || name.toLowerCase() !== "authorization");
  if (accessToken !== undefined) {
    headers.push(["Authorization", `Bearer ${accessToken}`]);
  }
  return fetchImpl(snapshot.url, { method: snapshot.method, headers, body: snapshot.body });
}

/** Wraps `fetch` so that it is always called with a URL and plain options. */
export function withPlainRequests(fetchImpl: FetchLike): FetchLike {
  return async (input, init) => sendSnapshot(fetchImpl, await snapshotRequest(input, init));
}

/** Source of access tokens for {@link withAuthRetry}. */
export interface AccessTokenSource {
  /** Refreshes the session (single-flight) and returns the new access token, or `null`. */
  refresh(): Promise<string | null>;
}

/**
 * Wraps `fetch` so that a 401 triggers one token refresh and one retry with the same body.
 *
 * The retry of `@mrp/api-client` (`onUnauthorized`) is not used: it re-creates the request from
 * the one that was already sent, which fails for requests with a body.
 */
export function withAuthRetry(fetchImpl: FetchLike, tokens: AccessTokenSource): FetchLike {
  return async (input, init) => {
    const snapshot = await snapshotRequest(input, init);
    const response = await sendSnapshot(fetchImpl, snapshot);
    if (response.status !== 401) {
      return response;
    }
    const token = await tokens.refresh();
    if (!token) {
      return response;
    }
    return sendSnapshot(fetchImpl, snapshot, token);
  };
}

/** Result shape of `openapi-fetch` calls. */
export interface CallResult<T> {
  data?: T;
  error?: unknown;
  response: Response;
}

/**
 * Awaits an API call and returns its data, or throws an {@link AppError}: connectivity problems
 * become `network`/`timeout`, error responses carry the status and the problem `code`.
 */
export async function unwrap<T>(call: Promise<CallResult<T>>): Promise<T> {
  let result: CallResult<T>;
  try {
    result = await call;
  } catch (error) {
    throw toAppError(error);
  }
  if (!result.response.ok) {
    throw errorFromResponse(result.response.status, result.error);
  }
  if (result.data === undefined) {
    throw new AppError({ kind: "http", status: result.response.status, code: "common.empty_response" });
  }
  return result.data;
}

/** Like {@link unwrap} for endpoints that return no body. */
export async function unwrapEmpty(call: Promise<CallResult<unknown>>): Promise<void> {
  let result: CallResult<unknown>;
  try {
    result = await call;
  } catch (error) {
    throw toAppError(error);
  }
  if (!result.response.ok) {
    throw errorFromResponse(result.response.status, result.error);
  }
}
