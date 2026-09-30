import { createApiClient, type ApiClient } from "@mrp/api-client";

import { getApiBaseUrl } from "@/config/env";
import { AppError } from "@/lib/errors";

import { withAuthRetry, withPlainRequests, withTimeout, type AccessTokenSource, type FetchLike } from "./http";

/** Token functions the authenticated client needs. */
export interface ClientTokens extends AccessTokenSource {
  /** Returns a valid access token, or `null` when signed out. */
  getAccessToken(): Promise<string | null>;
}

let tokenSource: ClientTokens | null = null;
let authenticated: ApiClient | null = null;
let anonymous: ApiClient | null = null;

/** Base URL of the API; throws a configuration error when it is missing or not secure. */
export function requireBaseUrl(): string {
  const result = getApiBaseUrl();
  if (!result.ok) {
    throw new AppError({ kind: "http", code: `app.config.base_url_${result.error}` });
  }
  return result.baseUrl;
}

/** Uses the platform `fetch` at call time (so tests can replace it). */
const platformFetch: FetchLike = (input, init) => fetch(input, init);

/** Registers the token manager used by the authenticated client. Call once at start-up. */
export function configureApi(tokens: ClientTokens): void {
  tokenSource = tokens;
  authenticated = null;
}

/** Client for public endpoints (login, refresh, logout). Sends no token. */
export function getAnonymousApi(): ApiClient {
  anonymous ??= createApiClient({ baseUrl: requireBaseUrl(), fetch: withPlainRequests(withTimeout(platformFetch)) });
  return anonymous;
}

/** Client that sends the bearer token and refreshes it once on 401. */
export function getApi(): ApiClient {
  if (!authenticated) {
    const tokens = tokenSource;
    if (!tokens) {
      throw new Error("configureApi() must be called before getApi()");
    }
    authenticated = createApiClient({
      baseUrl: requireBaseUrl(),
      getAccessToken: () => tokens.getAccessToken(),
      fetch: withAuthRetry(withTimeout(platformFetch), tokens),
    });
  }
  return authenticated;
}
