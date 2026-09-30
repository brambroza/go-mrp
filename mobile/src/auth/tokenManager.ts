import type { SecretStorage } from "@/storage/secureStorage";
import { REFRESH_TOKEN_KEY } from "@/storage/secureStorage";

/** Tokens returned by login and refresh. */
export interface TokenPair {
  /** Short-lived access token (kept in memory only). */
  accessToken: string;
  /** Rotating refresh token (kept in the secure store only). */
  refreshToken: string;
  /** Lifetime of the access token in seconds. */
  expiresIn: number;
}

/** Outcome of exchanging a refresh token. */
export type RefreshOutcome<TExtra = unknown> =
  | { ok: true; tokens: TokenPair; extra?: TExtra }
  /** The API refused the token: the session is over. */
  | { ok: false; reason: "rejected" }
  /** The API could not be reached: keep the session and try again later. */
  | { ok: false; reason: "unreachable" };

/** Dependencies of {@link TokenManager}. */
export interface TokenManagerOptions<TExtra = unknown> {
  /** Secure storage of the refresh token. */
  storage: SecretStorage;
  /** Calls `POST /auth/refresh`. Must not throw. */
  exchange: (refreshToken: string) => Promise<RefreshOutcome<TExtra>>;
  /** Called after a successful refresh with the extra payload (user profile). */
  onRefreshed?: (extra: TExtra | undefined) => void;
  /** Called once when the API rejects the refresh token; the app must sign the user out. */
  onSessionExpired?: () => void;
  /** Clock in milliseconds; injectable for tests. */
  now?: () => number;
  /** Refresh this many milliseconds before the access token expires. Default 30 s. */
  refreshSkewMs?: number;
}

/**
 * Keeps the access token in memory and the refresh token in the secure store.
 *
 * The API rotates the refresh token on every use and revokes all sessions when an old token is
 * used again. Therefore:
 * - refresh is single-flight: concurrent callers share one request;
 * - the new refresh token is written to the secure store before the refresh resolves, so it can
 *   never be used again before it is persisted.
 */
export class TokenManager<TExtra = unknown> {
  private accessToken: string | null = null;
  private accessExpiresAt = 0;
  private inFlight: Promise<string | null> | null = null;
  private generation = 0;

  private readonly storage: SecretStorage;
  private readonly exchange: (refreshToken: string) => Promise<RefreshOutcome<TExtra>>;
  private readonly onRefreshed: ((extra: TExtra | undefined) => void) | undefined;
  private readonly onSessionExpired: (() => void) | undefined;
  private readonly now: () => number;
  private readonly refreshSkewMs: number;

  constructor(options: TokenManagerOptions<TExtra>) {
    this.storage = options.storage;
    this.exchange = options.exchange;
    this.onRefreshed = options.onRefreshed;
    this.onSessionExpired = options.onSessionExpired;
    this.now = options.now ?? Date.now;
    this.refreshSkewMs = options.refreshSkewMs ?? 30_000;
  }

  /** Stores the tokens of a fresh login. The refresh token is persisted before this resolves. */
  async setSession(tokens: TokenPair): Promise<void> {
    this.generation += 1;
    await this.storage.set(REFRESH_TOKEN_KEY, tokens.refreshToken);
    this.accessToken = tokens.accessToken;
    this.accessExpiresAt = this.now() + tokens.expiresIn * 1000;
  }

  /** Returns whether a refresh token is stored on the device. */
  async hasRefreshToken(): Promise<boolean> {
    return (await this.storage.get(REFRESH_TOKEN_KEY)) !== null;
  }

  /** Reads the stored refresh token; used only to revoke it on sign-out. */
  async readRefreshToken(): Promise<string | null> {
    return this.storage.get(REFRESH_TOKEN_KEY);
  }

  /** Returns the access token held in memory without refreshing. */
  peekAccessToken(): string | null {
    return this.accessToken;
  }

  /**
   * Returns a valid access token, refreshing first when it is missing or about to expire.
   * Returns `null` when signed out or when the API cannot be reached.
   */
  async getAccessToken(): Promise<string | null> {
    if (this.accessToken && this.now() < this.accessExpiresAt - this.refreshSkewMs) {
      return this.accessToken;
    }
    const refreshed = await this.refresh();
    if (refreshed) {
      return refreshed;
    }
    // Offline with a token that has not expired yet: still usable.
    return this.accessToken && this.now() < this.accessExpiresAt ? this.accessToken : null;
  }

  /**
   * Exchanges the refresh token for new tokens. Concurrent calls share one request.
   * Returns the new access token, or `null` when the session ended or the API is unreachable.
   */
  refresh(): Promise<string | null> {
    if (this.inFlight) {
      return this.inFlight;
    }
    const run = this.runRefresh().finally(() => {
      if (this.inFlight === run) {
        this.inFlight = null;
      }
    });
    this.inFlight = run;
    return run;
  }

  /** Forgets all tokens on this device. */
  async clear(): Promise<void> {
    this.generation += 1;
    this.accessToken = null;
    this.accessExpiresAt = 0;
    await this.storage.remove(REFRESH_TOKEN_KEY);
  }

  private async runRefresh(): Promise<string | null> {
    const generation = this.generation;
    const refreshToken = await this.storage.get(REFRESH_TOKEN_KEY);
    if (!refreshToken) {
      return null;
    }
    const outcome = await this.exchange(refreshToken);
    if (generation !== this.generation) {
      // The user signed out or signed in again while the request was running.
      return this.accessToken;
    }
    if (!outcome.ok) {
      if (outcome.reason === "rejected") {
        await this.clear();
        this.onSessionExpired?.();
      }
      return null;
    }
    // Persist the rotated token first: it must never be lost or reused.
    await this.storage.set(REFRESH_TOKEN_KEY, outcome.tokens.refreshToken);
    this.accessToken = outcome.tokens.accessToken;
    this.accessExpiresAt = this.now() + outcome.tokens.expiresIn * 1000;
    this.onRefreshed?.(outcome.extra);
    return this.accessToken;
  }
}
