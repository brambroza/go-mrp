import { createApiClient, type ApiClient } from "@mrp/api-client";

import { tokenManager } from "@/lib/auth/session";

/**
 * Base URL for data calls. Empty (the default) sends them to the same-origin proxy
 * `/api/v1/*` of this app; set `NEXT_PUBLIC_API_BASE_URL` to call the API directly.
 */
const baseUrl = (process.env.NEXT_PUBLIC_API_BASE_URL ?? "").replace(/\/+$/, "");

/**
 * Typed API client of the browser. Adds the in-memory access token to every request and, on
 * HTTP 401, refreshes once (single-flight) and repeats the request.
 */
export const api: ApiClient = createApiClient({
  baseUrl,
  getAccessToken: () => tokenManager.getAccessToken(),
  onUnauthorized: async () => {
    try {
      return await tokenManager.refresh();
    } catch {
      return null;
    }
  },
});

export type { ApiClient };
