import "server-only";

import type { Schemas } from "@mrp/api-client";
import { cookies } from "next/headers";

import { LOCALE_COOKIE, toLocale } from "@/i18n/config";
import type { Session } from "@/lib/auth/token-manager";
import { REFRESH_COOKIE } from "@/lib/auth/cookie-names";

import { cookieSecure } from "./env";

/** Lifetime of the refresh cookie; the API issues refresh tokens for 30 days. */
const REFRESH_MAX_AGE_SECONDS = 30 * 24 * 60 * 60;

/** Lifetime of the language cookie. */
const LOCALE_MAX_AGE_SECONDS = 365 * 24 * 60 * 60;

/** Refresh token of the current browser, or `undefined` when signed out. */
export async function readRefreshToken(): Promise<string | undefined> {
  const value = (await cookies()).get(REFRESH_COOKIE)?.value;
  return value && value.length >= 40 && value.length <= 200 ? value : undefined;
}

/** Removes the refresh cookie. */
export async function clearRefreshToken(): Promise<void> {
  (await cookies()).set(REFRESH_COOKIE, "", {
    httpOnly: true,
    secure: cookieSecure(),
    sameSite: "lax",
    path: "/",
    maxAge: 0,
  });
}

/**
 * Stores the refresh token in an `HttpOnly` cookie and returns what the browser may see:
 * the access token and the profile, never the refresh token.
 */
export async function startSession(tokens: Schemas["TokenResponse"]): Promise<Session> {
  const jar = await cookies();
  jar.set(REFRESH_COOKIE, tokens.refreshToken, {
    httpOnly: true,
    secure: cookieSecure(),
    sameSite: "lax",
    path: "/",
    maxAge: REFRESH_MAX_AGE_SECONDS,
  });
  if (!jar.get(LOCALE_COOKIE)) {
    jar.set(LOCALE_COOKIE, toLocale(tokens.user.language), {
      secure: cookieSecure(),
      sameSite: "lax",
      path: "/",
      maxAge: LOCALE_MAX_AGE_SECONDS,
    });
  }
  return { accessToken: tokens.accessToken, expiresIn: tokens.expiresIn, user: tokens.user };
}

/** Whether a parsed API body looks like a token response. */
export function isTokenResponse(body: unknown): body is Schemas["TokenResponse"] {
  if (typeof body !== "object" || body === null) {
    return false;
  }
  const candidate = body as Partial<Schemas["TokenResponse"]>;
  return (
    typeof candidate.accessToken === "string" &&
    typeof candidate.refreshToken === "string" &&
    typeof candidate.expiresIn === "number" &&
    typeof candidate.user === "object" &&
    candidate.user !== null
  );
}
