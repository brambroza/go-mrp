import "server-only";

import type { ApiProblem } from "@mrp/api-client";

/** Builds an RFC 7807 response in the same shape the API uses. */
export function problemResponse(status: number, code: string, detail: string, init?: ResponseInit): Response {
  const body: ApiProblem = { status, title: code, code, detail };
  const headers = new Headers(init?.headers);
  headers.set("content-type", "application/problem+json");
  headers.set("cache-control", "no-store");
  return new Response(JSON.stringify(body), { ...init, status, headers });
}

/** 403 for requests that fail the origin check. */
export function forbiddenOrigin(): Response {
  return problemResponse(403, "client.forbidden_origin", "Request origin is not allowed.");
}

/** 502 for an API that cannot be reached. */
export function upstreamUnavailable(): Response {
  return problemResponse(502, "client.upstream_unavailable", "The API is not reachable.");
}
