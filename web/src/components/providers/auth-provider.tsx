"use client";

import { useQueryClient } from "@tanstack/react-query";
import { createContext, useCallback, useContext, useEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from "react";

import { api } from "@/lib/api/client";
import { unwrap } from "@/lib/api/problem";
import { hasPermission, type PermissionRequirement } from "@/lib/auth/permissions";
import type { LoginValues, SignupValues } from "@/lib/auth/schemas";
import { onSignedOutElsewhere, signIn, signOut, signUp, tokenManager } from "@/lib/auth/session";
import type { Session, UserProfile } from "@/lib/auth/token-manager";

/** Where the session of this tab stands. */
export type AuthStatus = "loading" | "authenticated" | "unauthenticated" | "unavailable";

/** Value of the auth context. */
export interface AuthContextValue {
  /** `loading` until the first refresh finished; `unavailable` when the API could not be reached. */
  status: AuthStatus;
  /** Signed-in user, or `null`. */
  user: UserProfile | null;
  /** Restores the session from the refresh cookie; safe to call repeatedly. */
  restore: () => Promise<void>;
  /** Signs in and stores the session. */
  signIn: (values: LoginValues) => Promise<Session>;
  /** Creates a company and signs its owner in. */
  signUp: (values: SignupValues) => Promise<Session>;
  /** Signs out and clears every cached response. */
  signOut: () => Promise<void>;
  /** Loads the profile again, for example after 2FA was switched on. */
  reloadProfile: () => Promise<void>;
  /** Whether the user has the permission (any of them, for an array). */
  can: (permission: PermissionRequirement | null | undefined) => boolean;
}

const AuthContext = createContext<AuthContextValue | null>(null);

const subscribe = (listener: () => void) => tokenManager.subscribe(listener);
const getSnapshot = () => tokenManager.getSession();
const getServerSnapshot = () => null;

/** Holds the session of the tab and offers sign-in, sign-out and permission checks. */
export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const session = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
  const [restoreState, setRestoreState] = useState<"idle" | "done" | "failed">("idle");

  const restore = useCallback(async () => {
    if (tokenManager.getSession()) {
      setRestoreState("done");
      return;
    }
    try {
      await tokenManager.refresh();
      setRestoreState("done");
    } catch {
      setRestoreState("failed");
    }
  }, []);

  useEffect(
    () =>
      onSignedOutElsewhere(() => {
        tokenManager.setSession(null);
        queryClient.clear();
        setRestoreState("done");
      }),
    [queryClient],
  );

  const value = useMemo<AuthContextValue>(() => {
    const user = session?.user ?? null;
    const status: AuthStatus = user
      ? "authenticated"
      : restoreState === "idle"
        ? "loading"
        : restoreState === "failed"
          ? "unavailable"
          : "unauthenticated";
    return {
      status,
      user,
      restore,
      signIn: async (values) => {
        queryClient.clear();
        const next = await signIn(values);
        setRestoreState("done");
        return next;
      },
      signUp: async (values) => {
        queryClient.clear();
        const next = await signUp(values);
        setRestoreState("done");
        return next;
      },
      signOut: async () => {
        await signOut();
        queryClient.clear();
        setRestoreState("done");
      },
      reloadProfile: async () => {
        tokenManager.setUser(await unwrap(api.GET("/api/v1/auth/me")));
      },
      can: (permission) => hasPermission(user?.permissions, permission),
    };
  }, [session, restoreState, restore, queryClient]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

/** The auth context; throws outside of {@link AuthProvider}. */
export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) {
    throw new Error("useAuth must be used inside <AuthProvider>.");
  }
  return value;
}

/** The signed-in user; throws when used on a page that is not behind the sign-in guard. */
export function useUser(): UserProfile {
  const { user } = useAuth();
  if (!user) {
    throw new Error("useUser must be used on a page that requires sign-in.");
  }
  return user;
}
