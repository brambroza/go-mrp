import { create } from "zustand";

import { exchangeRefreshToken, login, logout, TWO_FACTOR_REQUIRED, type LoginInput, type UserProfile } from "@/api/auth";
import { configureApi } from "@/api/client";
import { AppError, toAppError } from "@/lib/errors";
import type { QueueScope } from "@/offline/types";
import { getDeviceKeyValueStorage } from "@/storage/keyValue";
import { createDeviceSecretStorage } from "@/storage/secureStorage";

import { TokenManager } from "./tokenManager";

const PROFILE_KEY = "session.profile";
const COMPANY_KEY = "login.companyCode";
const USER_NAME_KEY = "login.userName";

/** Phase of the session. */
export type SessionPhase = "restoring" | "signedOut" | "signedIn";

/** State of the session store. */
export interface SessionState {
  phase: SessionPhase;
  /** Profile of the signed-in user. */
  user: UserProfile | null;
  /** `true` when the session was restored from the device without reaching the API. */
  restoredOffline: boolean;
  /** `true` when the API ended the session (refresh token rejected); shown on the login screen. */
  expired: boolean;
}

/** Session store: who is signed in. Holds no tokens. */
export const useSession = create<SessionState>(() => ({ phase: "restoring", user: null, restoredOffline: false, expired: false }));

/** Listeners called after sign-out, to drop cached data of the previous user. */
const signOutListeners = new Set<() => void>();

/** Registers a function that clears in-memory data when the user signs out. */
export function onSignedOut(listener: () => void): () => void {
  signOutListeners.add(listener);
  return () => signOutListeners.delete(listener);
}

/** Token manager of the app: access token in memory, refresh token in the secure store. */
export const tokenManager = new TokenManager<UserProfile>({
  storage: createDeviceSecretStorage(),
  exchange: exchangeRefreshToken,
  onRefreshed: (profile) => {
    // A refresh that finishes after sign-out is dropped by the token manager, so any profile
    // that arrives here belongs to the current session (restoring or signed in).
    if (profile && useSession.getState().phase !== "signedOut") {
      useSession.setState({ phase: "signedIn", user: profile, restoredOffline: false, expired: false });
      void cacheProfile(profile);
    }
  },
  onSessionExpired: () => {
    void clearLocalSession(true);
  },
});

configureApi(tokenManager);

/** Stores the profile (no secrets) so that the app can open without a connection. */
async function cacheProfile(profile: UserProfile): Promise<void> {
  try {
    await getDeviceKeyValueStorage().set(PROFILE_KEY, JSON.stringify(profile));
  } catch {
    // The cache is a convenience; the session works without it.
  }
}

/** Reads the cached profile. */
async function readCachedProfile(): Promise<UserProfile | null> {
  try {
    const text = await getDeviceKeyValueStorage().get(PROFILE_KEY);
    if (!text) {
      return null;
    }
    const profile = JSON.parse(text) as Partial<UserProfile>;
    const valid = typeof profile.id === "string" && typeof profile.tenantId === "string" && Array.isArray(profile.permissions);
    return valid ? (profile as UserProfile) : null;
  } catch {
    return null;
  }
}

/** Removes tokens, cached profile and in-memory data of the user from the device. */
async function clearLocalSession(expired: boolean): Promise<void> {
  await tokenManager.clear().catch(() => undefined);
  await getDeviceKeyValueStorage()
    .remove(PROFILE_KEY)
    .catch(() => undefined);
  useSession.setState({ phase: "signedOut", user: null, restoredOffline: false, expired });
  signOutListeners.forEach((listener) => listener());
}

/**
 * Restores the session at app start: exchanges the stored refresh token; when the API cannot be
 * reached, the cached profile is used so that warehouse work can continue offline.
 */
export async function restoreSession(): Promise<void> {
  try {
    if (!(await tokenManager.hasRefreshToken())) {
      useSession.setState({ phase: "signedOut", user: null, restoredOffline: false });
      return;
    }
    const cached = await readCachedProfile();
    if (cached) {
      // Open the app at once; the refresh below replaces the profile when the API answers.
      useSession.setState({ phase: "signedIn", user: cached, restoredOffline: true });
    }
    const token = await tokenManager.refresh();
    if (token || useSession.getState().phase === "signedOut") {
      return; // refreshed (profile set by onRefreshed), or rejected by the API
    }
    if (!cached) {
      // The API is unreachable and nothing is cached: the user has to sign in when online.
      useSession.setState({ phase: "signedOut", user: null, restoredOffline: false });
    }
  } catch {
    useSession.setState({ phase: "signedOut", user: null, restoredOffline: false });
  }
}

/** Result of {@link signIn}. */
export type SignInResult = { ok: true } | { ok: false; twoFactorRequired: true } | { ok: false; twoFactorRequired: false; error: AppError };

/** Signs in and stores the session. The password is never stored. */
export async function signIn(input: LoginInput): Promise<SignInResult> {
  try {
    const response = await login(input);
    await tokenManager.setSession({ accessToken: response.accessToken, refreshToken: response.refreshToken, expiresIn: response.expiresIn });
    await cacheProfile(response.user);
    await rememberLogin(input.companyCode, input.userName);
    useSession.setState({ phase: "signedIn", user: response.user, restoredOffline: false, expired: false });
    return { ok: true };
  } catch (error) {
    const appError = toAppError(error);
    if (appError.code === TWO_FACTOR_REQUIRED) {
      return { ok: false, twoFactorRequired: true };
    }
    return { ok: false, twoFactorRequired: false, error: appError };
  }
}

/** Signs out: revokes the refresh token on the server (best effort) and clears the device. */
export async function signOut(): Promise<void> {
  const refreshToken = await tokenManager.readRefreshToken().catch(() => null);
  if (refreshToken) {
    await logout(refreshToken).catch(() => undefined);
  }
  await clearLocalSession(false);
}

/** Remembers the company code and user name for the next sign-in. */
async function rememberLogin(companyCode: string, userName: string): Promise<void> {
  try {
    const storage = getDeviceKeyValueStorage();
    await storage.set(COMPANY_KEY, companyCode);
    await storage.set(USER_NAME_KEY, userName);
  } catch {
    // Remembering is optional.
  }
}

/** Reads the remembered company code and user name. */
export async function readRememberedLogin(): Promise<{ companyCode: string; userName: string }> {
  try {
    const storage = getDeviceKeyValueStorage();
    return { companyCode: (await storage.get(COMPANY_KEY)) ?? "", userName: (await storage.get(USER_NAME_KEY)) ?? "" };
  } catch {
    return { companyCode: "", userName: "" };
  }
}

/** Queue scope of a profile. */
export function scopeOf(user: UserProfile | null): QueueScope | null {
  return user ? { tenantId: user.tenantId, userId: user.id } : null;
}
