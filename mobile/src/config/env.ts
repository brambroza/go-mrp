/** Why the configured API base URL was rejected. */
export type BaseUrlError = "missing" | "invalid" | "insecure";

/** Result of {@link validateBaseUrl}. */
export type BaseUrlResult = { ok: true; baseUrl: string } | { ok: false; error: BaseUrlError };

/** Returns whether the host is the device itself or a private (LAN) address. */
export function isLocalHost(hostname: string): boolean {
  const host = hostname.toLowerCase().replace(/^\[|\]$/g, "");
  if (host === "localhost" || host === "::1" || host.endsWith(".local") || host.endsWith(".localhost")) {
    return true;
  }
  const match = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/.exec(host);
  if (!match) {
    return false;
  }
  const [a, b] = [Number(match[1]), Number(match[2])];
  // 10.0.2.2 is the Android emulator's alias of the host machine (covered by 10/8).
  return a === 127 || a === 10 || (a === 192 && b === 168) || (a === 172 && b >= 16 && b <= 31) || (a === 169 && b === 254);
}

/**
 * Validates the API base URL: it must be an absolute http(s) URL without credentials, and any
 * host that is not local/private must use HTTPS.
 */
export function validateBaseUrl(raw: string | null | undefined): BaseUrlResult {
  const text = (raw ?? "").trim();
  if (text === "") {
    return { ok: false, error: "missing" };
  }
  const match = /^(https?):\/\/(\[[0-9a-f:]+\]|[a-z0-9.-]+)(?::(\d{1,5}))?(\/[^\s?#]*)?$/i.exec(text);
  if (!match) {
    return { ok: false, error: "invalid" };
  }
  const scheme = (match[1] ?? "").toLowerCase();
  const host = match[2] ?? "";
  if (scheme !== "https" && !isLocalHost(host)) {
    return { ok: false, error: "insecure" };
  }
  return { ok: true, baseUrl: text.replace(/\/+$/, "") };
}

/** API base URL from `EXPO_PUBLIC_API_BASE_URL`, validated once. */
export function getApiBaseUrl(): BaseUrlResult {
  return validateBaseUrl(process.env.EXPO_PUBLIC_API_BASE_URL);
}
