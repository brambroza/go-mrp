import type { Schemas } from "@mrp/api-client";

/** Signed-in user with tenant and permissions. */
export type UserProfile = Schemas["UserProfile"];

/** What the BFF returns after sign-in or refresh; the refresh token never reaches the browser. */
export interface Session {
  /** Short-lived JWT, kept in memory only. */
  accessToken: string;
  /** Lifetime of the access token in seconds. */
  expiresIn: number;
  /** Current user. */
  user: UserProfile;
}

/** Called whenever the session changes; `null` means signed out. */
export type SessionListener = (session: Session | null) => void;

/** Options of {@link createTokenManager}. */
export interface TokenManagerOptions {
  /** Exchanges the refresh cookie for a new session; resolves `null` when the user must sign in again. */
  refresh: () => Promise<Session | null>;
  /** Clock, replaceable in tests. */
  now?: () => number;
  /** Refresh this many milliseconds before the token expires (default 30 s). */
  leewayMs?: number;
}

/** In-memory holder of the access token with single-flight refresh. */
export interface TokenManager {
  /** Current session, or `null`. */
  getSession(): Session | null;
  /** Access token for the next request; refreshes first when the token is about to expire. */
  getAccessToken(): Promise<string | null>;
  /** Stores a session obtained from sign-in. */
  setSession(session: Session | null): void;
  /** Replaces the user of the current session (after the profile changed). */
  setUser(user: UserProfile): void;
  /**
   * Obtains a new session. Concurrent calls share one request: ten requests failing with 401 at
   * the same time trigger exactly one refresh, which matters because refresh tokens rotate and
   * reusing an old one revokes every session of the user.
   */
  refresh(): Promise<string | null>;
  /** Registers a listener; returns the function that removes it. */
  subscribe(listener: SessionListener): () => void;
}

/** Creates the token manager. */
export function createTokenManager(options: TokenManagerOptions): TokenManager {
  const now = options.now ?? (() => Date.now());
  const leewayMs = options.leewayMs ?? 30_000;
  const listeners = new Set<SessionListener>();
  let session: Session | null = null;
  let expiresAt = 0;
  let inFlight: Promise<string | null> | null = null;

  const notify = () => {
    for (const listener of listeners) {
      listener(session);
    }
  };

  const store = (next: Session | null) => {
    session = next;
    expiresAt = next ? now() + next.expiresIn * 1000 : 0;
    notify();
  };

  const refresh = (): Promise<string | null> => {
    if (inFlight) {
      return inFlight;
    }
    inFlight = (async () => {
      try {
        const next = await options.refresh();
        store(next);
        return next?.accessToken ?? null;
      } finally {
        inFlight = null;
      }
    })();
    return inFlight;
  };

  return {
    getSession: () => session,
    async getAccessToken() {
      if (!session) {
        return null;
      }
      if (now() >= expiresAt - leewayMs) {
        try {
          return await refresh();
        } catch {
          // The network failed; let the request go out with the old token and fail on its own.
          return session?.accessToken ?? null;
        }
      }
      return session.accessToken;
    },
    setSession: store,
    setUser(user) {
      if (session) {
        session = { ...session, user };
        notify();
      }
    },
    refresh,
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
  };
}
