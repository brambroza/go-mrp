import "server-only";

import { allowedOrigins } from "./env";

/** Parts of a request needed for the origin check. */
export interface OriginCheckInput {
  /** `Origin` header. */
  origin: string | null;
  /** `Host` header. */
  host: string | null;
  /** `X-Forwarded-Host` header set by the load balancer. */
  forwardedHost: string | null;
  /** `Sec-Fetch-Site` header. */
  secFetchSite: string | null;
  /** Origins from configuration that are always accepted. */
  allowed: readonly string[];
}

/**
 * CSRF defence for the cookie-backed handlers. A state-changing request is accepted only when the
 * browser says it comes from this site: the `Origin` host equals the host the request was sent to
 * (or a configured origin). Requests without `Origin` are accepted only when `Sec-Fetch-Site` is
 * `same-origin`. The refresh cookie is `SameSite=Lax` as the first line of defence; this is the second.
 */
export function isTrustedOrigin(input: OriginCheckInput): boolean {
  if (input.secFetchSite && input.secFetchSite !== "same-origin" && input.secFetchSite !== "none") {
    return false;
  }
  if (!input.origin) {
    return input.secFetchSite === "same-origin";
  }
  let originUrl: URL;
  try {
    originUrl = new URL(input.origin);
  } catch {
    return false;
  }
  const origin = originUrl.origin.toLowerCase();
  if (input.allowed.includes(origin)) {
    return true;
  }
  const originHost = originUrl.host.toLowerCase();
  const hosts = [input.host, ...(input.forwardedHost?.split(",") ?? [])]
    .map((host) => host?.trim().toLowerCase())
    .filter((host): host is string => Boolean(host));
  return hosts.includes(originHost);
}

/** Runs {@link isTrustedOrigin} on a request. */
export function requestHasTrustedOrigin(request: Request): boolean {
  return isTrustedOrigin({
    origin: request.headers.get("origin"),
    host: request.headers.get("host"),
    forwardedHost: request.headers.get("x-forwarded-host"),
    secFetchSite: request.headers.get("sec-fetch-site"),
    allowed: allowedOrigins(),
  });
}
