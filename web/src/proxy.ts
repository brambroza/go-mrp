import { NextResponse, type NextRequest } from "next/server";

import { REFRESH_COOKIE } from "@/lib/auth/cookie-names";
import { buildContentSecurityPolicy, originOf } from "@/lib/security/csp";

/** Pages that can be opened without signing in. */
const PUBLIC_PATHS = ["/login", "/signup"];

function isPublicPath(pathname: string): boolean {
  return PUBLIC_PATHS.some((path) => pathname === path || pathname.startsWith(`${path}/`));
}

/**
 * Runs before every page request:
 * 1. sends visitors without a refresh cookie to `/login` (an optimistic check — the API is the
 *    authority and rejects every call without a valid token);
 * 2. sets the Content-Security-Policy with a fresh nonce.
 */
export function proxy(request: NextRequest): NextResponse {
  const { pathname, search } = request.nextUrl;

  if (!isPublicPath(pathname) && !request.cookies.has(REFRESH_COOKIE)) {
    const login = new URL("/login", request.url);
    if (pathname !== "/") {
      login.searchParams.set("next", `${pathname}${search}`);
    }
    return NextResponse.redirect(login);
  }

  const nonce = Buffer.from(crypto.randomUUID()).toString("base64");
  const policy = buildContentSecurityPolicy({
    nonce,
    development: process.env.NODE_ENV === "development",
    apiOrigin: originOf(process.env.NEXT_PUBLIC_API_BASE_URL),
  });

  const requestHeaders = new Headers(request.headers);
  requestHeaders.set("x-nonce", nonce);
  requestHeaders.set("Content-Security-Policy", policy);

  const response = NextResponse.next({ request: { headers: requestHeaders } });
  response.headers.set("Content-Security-Policy", policy);
  return response;
}

/** Skips API routes, static files and link prefetches. */
export const config = {
  matcher: [
    {
      source: "/((?!api/|_next/static|_next/image|favicon.ico|.*\\.(?:png|jpg|jpeg|svg|webp|ico|txt|webmanifest)$).*)",
      missing: [
        { type: "header", key: "next-router-prefetch" },
        { type: "header", key: "purpose", value: "prefetch" },
      ],
    },
  ],
};
