/** Inputs of {@link buildContentSecurityPolicy}. */
export interface CspOptions {
  /** Per-request nonce for inline scripts. */
  nonce: string;
  /** Development mode: React needs `eval` for debugging and the dev server a websocket. */
  development: boolean;
  /** Origin of the API when the browser calls it directly; omit when requests go through the proxy. */
  apiOrigin?: string | null;
}

/** Origin (`scheme://host:port`) of a URL, or `null` when it is empty or not absolute. */
export function originOf(url: string | null | undefined): string | null {
  if (!url) {
    return null;
  }
  try {
    return new URL(url).origin;
  } catch {
    return null;
  }
}

/**
 * Content-Security-Policy of the pages. Scripts need the nonce; styles allow inline because
 * Radix and `next/font` set style attributes at runtime; nothing may frame the app.
 */
export function buildContentSecurityPolicy(options: CspOptions): string {
  const connect = ["'self'", options.apiOrigin, options.development ? "ws:" : null].filter(Boolean).join(" ");
  const directives = [
    "default-src 'self'",
    `script-src 'self' 'nonce-${options.nonce}' 'strict-dynamic'${options.development ? " 'unsafe-eval'" : ""}`,
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob:",
    "font-src 'self' data:",
    `connect-src ${connect}`,
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
  ];
  return directives.join("; ");
}
