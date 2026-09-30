import "server-only";

import { apiBaseUrl } from "./env";

/** Milliseconds after which a call to the API is aborted. */
const UPSTREAM_TIMEOUT_MS = 30_000;

/** Headers that tell the API who the real client is; the API rate-limits sign-in per client address. */
export function forwardedHeaders(request: Request): Record<string, string> {
  const headers: Record<string, string> = {};
  const forwardedFor = request.headers.get("x-forwarded-for");
  if (forwardedFor) {
    headers["x-forwarded-for"] = forwardedFor;
  }
  const language = request.headers.get("accept-language");
  if (language) {
    headers["accept-language"] = language;
  }
  return headers;
}

/** Calls the API with a JSON body and returns the raw response; throws when the API is unreachable. */
export async function postJson(request: Request, path: string, body: unknown): Promise<Response> {
  return fetch(`${apiBaseUrl()}${path}`, {
    method: "POST",
    headers: {
      ...forwardedHeaders(request),
      accept: "application/json",
      "content-type": "application/json",
    },
    body: JSON.stringify(body),
    cache: "no-store",
    redirect: "manual",
    signal: AbortSignal.timeout(UPSTREAM_TIMEOUT_MS),
  });
}

/** Copies status and JSON body of an API response to the browser, dropping every other header. */
export async function relay(upstream: Response): Promise<Response> {
  const headers = new Headers({ "cache-control": "no-store" });
  const contentType = upstream.headers.get("content-type");
  if (contentType) {
    headers.set("content-type", contentType);
  }
  const retryAfter = upstream.headers.get("retry-after");
  if (retryAfter) {
    headers.set("retry-after", retryAfter);
  }
  if (upstream.status === 204 || upstream.status === 304) {
    return new Response(null, { status: upstream.status, headers });
  }
  return new Response(await upstream.arrayBuffer(), { status: upstream.status, headers });
}
