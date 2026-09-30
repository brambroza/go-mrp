import "server-only";

import { apiBaseUrl } from "./env";
import { problemResponse, upstreamUnavailable } from "./problem";
import { forwardedHeaders, relay } from "./upstream";

/** Largest request body the proxy forwards (documents with many lines stay far below this). */
const MAX_BODY_BYTES = 2 * 1024 * 1024;

/** Milliseconds after which a proxied call is aborted. */
const PROXY_TIMEOUT_MS = 60_000;

/**
 * Endpoints that hand out or accept refresh tokens. The browser must use `/api/auth/*` instead,
 * so that refresh tokens only ever live in the `HttpOnly` cookie.
 */
const TOKEN_ENDPOINTS = new Set(["auth/login", "auth/signup", "auth/refresh", "auth/logout"]);

/** Whether a path segment is safe to forward (no traversal, no encoded separators). */
function isSafeSegment(segment: string): boolean {
  return segment.length > 0 && segment.length <= 200 && segment !== "." && segment !== ".." && !/[\\/\x00-\x1f]/.test(segment);
}

/**
 * Forwards a data request to `/api/v1/*` of the .NET API. Authentication is the bearer token the
 * browser sends; cookies are never forwarded and never turned into credentials, so a cross-site
 * request cannot act as the user (no CSRF surface).
 */
export async function proxyToApi(request: Request, path: readonly string[]): Promise<Response> {
  if (path.length === 0 || path.length > 12 || !path.every(isSafeSegment)) {
    return problemResponse(404, "common.not_found", "Not found.");
  }
  if (TOKEN_ENDPOINTS.has(path.join("/").toLowerCase())) {
    return problemResponse(404, "common.not_found", "Use /api/auth for signing in and out.");
  }

  const headers: Record<string, string> = { ...forwardedHeaders(request), accept: "application/json" };
  const authorization = request.headers.get("authorization");
  if (authorization) {
    headers.authorization = authorization;
  }

  let body: ArrayBuffer | undefined;
  if (request.method !== "GET" && request.method !== "HEAD") {
    const declared = Number(request.headers.get("content-length") ?? "0");
    if (declared > MAX_BODY_BYTES) {
      return problemResponse(413, "client.payload_too_large", "Request body is too large.");
    }
    body = await request.arrayBuffer();
    if (body.byteLength > MAX_BODY_BYTES) {
      return problemResponse(413, "client.payload_too_large", "Request body is too large.");
    }
    headers["content-type"] = request.headers.get("content-type") ?? "application/json";
  }

  const target = `${apiBaseUrl()}/api/v1/${path.map(encodeURIComponent).join("/")}${new URL(request.url).search}`;
  try {
    const upstream = await fetch(target, {
      method: request.method,
      headers,
      body: body && body.byteLength > 0 ? body : undefined,
      cache: "no-store",
      redirect: "manual",
      signal: AbortSignal.timeout(PROXY_TIMEOUT_MS),
    });
    return await relay(upstream);
  } catch {
    return upstreamUnavailable();
  }
}
