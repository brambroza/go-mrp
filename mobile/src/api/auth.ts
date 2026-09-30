import type { Schemas } from "@mrp/api-client";

import type { RefreshOutcome } from "@/auth/tokenManager";
import { AppError, toAppError } from "@/lib/errors";

import { getAnonymousApi, getApi } from "./client";
import { unwrap, unwrapEmpty } from "./http";

/** User profile with tenant and permissions. */
export type UserProfile = Schemas["UserProfile"];

/** Tokens and profile returned by login and refresh. */
export type TokenResponse = Schemas["TokenResponse"];

/** Input of {@link login}. */
export interface LoginInput {
  companyCode: string;
  userName: string;
  password: string;
  /** TOTP code; sent only on the second step. */
  twoFactorCode?: string;
  /** Device label shown in the session list. */
  device?: string;
}

/** Error code of the API that asks for the TOTP code. */
export const TWO_FACTOR_REQUIRED = "platform.auth.two_factor_required";

/** Signs in. Throws {@link AppError} with code {@link TWO_FACTOR_REQUIRED} when a TOTP code is needed. */
export function login(input: LoginInput): Promise<TokenResponse> {
  return unwrap(
    getAnonymousApi().POST("/api/v1/auth/login", {
      body: {
        companyCode: input.companyCode,
        userName: input.userName,
        password: input.password,
        twoFactorCode: input.twoFactorCode ?? null,
        device: input.device ?? null,
      },
    }),
  );
}

/**
 * Exchanges a refresh token. Never throws: a refusal by the API ends the session, while
 * connectivity or server trouble keeps it.
 */
export async function exchangeRefreshToken(refreshToken: string): Promise<RefreshOutcome<UserProfile>> {
  try {
    const response = await unwrap(getAnonymousApi().POST("/api/v1/auth/refresh", { body: { refreshToken } }));
    return {
      ok: true,
      tokens: { accessToken: response.accessToken, refreshToken: response.refreshToken, expiresIn: response.expiresIn },
      extra: response.user,
    };
  } catch (error) {
    const appError = error instanceof AppError ? error : toAppError(error);
    const refused = appError.status !== undefined && appError.status >= 400 && appError.status < 500 && appError.status !== 408 && appError.status !== 429;
    return { ok: false, reason: refused ? "rejected" : "unreachable" };
  }
}

/** Revokes the refresh token on the server. */
export function logout(refreshToken: string): Promise<void> {
  return unwrapEmpty(getAnonymousApi().POST("/api/v1/auth/logout", { body: { refreshToken } }));
}

/** Reads the profile of the signed-in user. */
export function fetchProfile(): Promise<UserProfile> {
  return unwrap(getApi().GET("/api/v1/auth/me"));
}
