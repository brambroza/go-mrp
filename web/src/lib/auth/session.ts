import type { LoginValues, SignupValues } from "./schemas";
import { createTokenManager, type Session } from "./token-manager";
import { ApiError, normalizeError, toApiError } from "@/lib/api/problem";

/** Name of the cross-tab lock that serialises refreshes, and of the sign-out broadcast channel. */
const AUTH_CHANNEL = "mrp-auth";

/** Sends a JSON `POST` to a same-origin auth handler. */
async function postAuth(path: string, body?: unknown): Promise<Response> {
  try {
    return await fetch(path, {
      method: "POST",
      credentials: "same-origin",
      cache: "no-store",
      headers: body === undefined ? undefined : { "content-type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch (error) {
    throw normalizeError(error);
  }
}

/** Reads the session of a successful response or throws the problem of a failed one. */
async function readSession(response: Response): Promise<Session> {
  const body: unknown = await response.json().catch(() => undefined);
  if (!response.ok) {
    throw toApiError(body, response.status);
  }
  return body as Session;
}

/** Runs a task while holding a cross-tab lock, where the browser supports Web Locks. */
async function withRefreshLock<T>(task: () => Promise<T>): Promise<T> {
  if (typeof navigator !== "undefined" && "locks" in navigator) {
    return navigator.locks.request(AUTH_CHANNEL, task) as Promise<T>;
  }
  return task();
}

/**
 * Exchanges the refresh cookie for a new session. Resolves `null` when the user must sign in
 * again and throws when the failure is temporary (API down, rate limit), so that an outage
 * does not sign anybody out. Tabs take turns through a Web Lock: refresh tokens rotate, and two
 * tabs presenting the same token at once would revoke every session of the user.
 */
export async function requestRefresh(): Promise<Session | null> {
  return withRefreshLock(async () => {
    const response = await postAuth("/api/auth/refresh");
    if (response.status === 400 || response.status === 401 || response.status === 403) {
      return null;
    }
    return readSession(response);
  });
}

/** The access token of this tab. Lives in memory only and is gone after a reload. */
export const tokenManager = createTokenManager({ refresh: requestRefresh });

/** Tells the other tabs of this browser that the user signed out. */
function broadcastSignOut(): void {
  if (typeof BroadcastChannel === "undefined") {
    return;
  }
  const channel = new BroadcastChannel(AUTH_CHANNEL);
  channel.postMessage("signed-out");
  channel.close();
}

/** Calls `listener` when another tab signs out; returns the function that stops listening. */
export function onSignedOutElsewhere(listener: () => void): () => void {
  if (typeof BroadcastChannel === "undefined") {
    return () => undefined;
  }
  const channel = new BroadcastChannel(AUTH_CHANNEL);
  channel.onmessage = (event: MessageEvent<unknown>) => {
    if (event.data === "signed-out") {
      listener();
    }
  };
  return () => channel.close();
}

/** Signs in; throws an {@link ApiError} (code `platform.auth.two_factor_required` asks for the TOTP code). */
export async function signIn(values: LoginValues): Promise<Session> {
  const session = await readSession(await postAuth("/api/auth/login", values));
  tokenManager.setSession(session);
  return session;
}

/** Creates a company and signs its owner in. */
export async function signUp(values: SignupValues): Promise<Session> {
  const session = await readSession(await postAuth("/api/auth/signup", values));
  tokenManager.setSession(session);
  return session;
}

/** Signs out everywhere in this browser. Never throws: the local session is dropped in any case. */
export async function signOut(): Promise<void> {
  try {
    await postAuth("/api/auth/logout");
  } catch (error) {
    if (!(error instanceof ApiError)) {
      throw error;
    }
  } finally {
    tokenManager.setSession(null);
    broadcastSignOut();
  }
}
