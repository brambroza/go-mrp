import createClient, { type Client, type Middleware } from "openapi-fetch";
import type { components, paths } from "./schema";

export type { components, paths };

/** Schemas of the API, e.g. `Schemas["ItemDto"]`. */
export type Schemas = components["schemas"];

/** Error body returned by the API (RFC 7807) with the i18n error code. */
export interface ApiProblem {
  status?: number;
  title?: string;
  detail?: string;
  /** Stable error code, e.g. `inventory.insufficient_stock`; translate it on the client. */
  code?: string;
  /** Field errors of a validation problem, keyed by camelCase field path. */
  errors?: Record<string, string[]>;
}

/** Options of {@link createApiClient}. */
export interface ApiClientOptions {
  /** Base URL of the API host without the `/api/v1` suffix, e.g. `https://api.example.com`. */
  baseUrl: string;
  /** Returns the current access token, or `null` when signed out. */
  getAccessToken?: () => string | null | Promise<string | null>;
  /**
   * Called once when a request fails with 401. Return a fresh access token to retry the request,
   * or `null` to give up (the caller should then sign the user out).
   */
  onUnauthorized?: () => Promise<string | null>;
  /** Custom fetch implementation (tests, React Native). */
  fetch?: typeof fetch;
}

/** Typed API client generated from `openapi.json`. */
export type ApiClient = Client<paths>;

/**
 * Creates a typed client that adds the bearer token to every request and retries once after
 * refreshing the token on HTTP 401.
 */
export function createApiClient(options: ApiClientOptions): ApiClient {
  const client = createClient<paths>({ baseUrl: options.baseUrl, fetch: options.fetch });

  const auth: Middleware = {
    async onRequest({ request }) {
      const token = await options.getAccessToken?.();
      if (token) {
        request.headers.set("Authorization", `Bearer ${token}`);
      }
      return request;
    },
    async onResponse({ request, response }) {
      if (response.status !== 401 || !options.onUnauthorized || request.headers.has("x-mrp-retry")) {
        return response;
      }
      const token = await options.onUnauthorized();
      if (!token) {
        return response;
      }
      const retry = new Request(request, { headers: new Headers(request.headers) });
      retry.headers.set("Authorization", `Bearer ${token}`);
      retry.headers.set("x-mrp-retry", "1");
      return (options.fetch ?? fetch)(retry);
    },
  };

  client.use(auth);
  return client;
}

/** Narrows an unknown error body to {@link ApiProblem}. */
export function asProblem(error: unknown): ApiProblem {
  return typeof error === "object" && error !== null ? (error as ApiProblem) : {};
}
