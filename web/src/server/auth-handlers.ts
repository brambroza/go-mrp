import "server-only";

import type { Schemas } from "@mrp/api-client";
import type { ZodType } from "zod";

import { requestHasTrustedOrigin } from "./origin";
import { forbiddenOrigin, problemResponse, upstreamUnavailable } from "./problem";
import { clearRefreshToken, isTokenResponse, readRefreshToken, startSession } from "./session-cookie";
import { postJson, relay } from "./upstream";

/** Largest request body the auth handlers read. */
const MAX_BODY_BYTES = 8 * 1024;

/** Reads a small JSON body; returns `undefined` when it is missing, too large or not JSON. */
async function readJson(request: Request): Promise<unknown> {
  const text = await request.text();
  if (text.length === 0 || text.length > MAX_BODY_BYTES) {
    return undefined;
  }
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return undefined;
  }
}

/** Converts zod issues to the `errors` map of a validation problem. */
function validationProblem(issues: readonly { path: PropertyKey[]; message: string }[]): Response {
  const errors: Record<string, string[]> = {};
  for (const issue of issues) {
    const key = issue.path.map(String).join(".") || "body";
    (errors[key] ??= []).push(issue.message);
  }
  return new Response(JSON.stringify({ status: 400, title: "One or more validation errors occurred.", errors }), {
    status: 400,
    headers: { "content-type": "application/problem+json", "cache-control": "no-store" },
  });
}

/** Turns a successful token response of the API into a session; relays everything else. */
async function finishSignIn(upstream: Response): Promise<Response> {
  if (!upstream.ok) {
    return relay(upstream);
  }
  const body: unknown = await upstream.json().catch(() => undefined);
  if (!isTokenResponse(body)) {
    return upstreamUnavailable();
  }
  const session = await startSession(body);
  return Response.json(session, { headers: { "cache-control": "no-store" } });
}

/**
 * Shared implementation of `POST /api/auth/login` and `/signup`: checks the origin, validates the
 * body with the same schema the form uses, calls the API and stores the refresh token in the cookie.
 */
export async function signInWith<TValues>(
  request: Request,
  apiPath: string,
  schema: ZodType<TValues>,
  toApiBody: (values: TValues, device: string | null) => unknown,
): Promise<Response> {
  if (!requestHasTrustedOrigin(request)) {
    return forbiddenOrigin();
  }
  const parsed = schema.safeParse(await readJson(request));
  if (!parsed.success) {
    return validationProblem(parsed.error.issues);
  }
  const device = request.headers.get("user-agent")?.slice(0, 200) ?? null;
  try {
    return await finishSignIn(await postJson(request, apiPath, toApiBody(parsed.data, device)));
  } catch {
    return upstreamUnavailable();
  }
}

/** Implementation of `POST /api/auth/refresh`: rotates the refresh cookie and returns a new access token. */
export async function refreshSession(request: Request): Promise<Response> {
  if (!requestHasTrustedOrigin(request)) {
    return forbiddenOrigin();
  }
  const refreshToken = await readRefreshToken();
  if (!refreshToken) {
    return problemResponse(401, "platform.auth.invalid_refresh_token", "Not signed in.");
  }
  let upstream: Response;
  try {
    const body: Schemas["RefreshRequest"] = { refreshToken };
    upstream = await postJson(request, "/api/v1/auth/refresh", body);
  } catch {
    // Keep the cookie: the API being down must not sign everybody out.
    return upstreamUnavailable();
  }
  if (upstream.status >= 400 && upstream.status < 500 && upstream.status !== 429) {
    await clearRefreshToken();
  }
  return finishSignIn(upstream);
}

/** Implementation of `POST /api/auth/logout`: revokes the refresh token and removes the cookie. */
export async function endSession(request: Request): Promise<Response> {
  if (!requestHasTrustedOrigin(request)) {
    return forbiddenOrigin();
  }
  const refreshToken = await readRefreshToken();
  await clearRefreshToken();
  if (refreshToken) {
    try {
      const body: Schemas["RefreshRequest"] = { refreshToken };
      await postJson(request, "/api/v1/auth/logout", body);
    } catch {
      // The cookie is gone; the token expires on its own if the API could not be told.
    }
  }
  return new Response(null, { status: 204, headers: { "cache-control": "no-store" } });
}
