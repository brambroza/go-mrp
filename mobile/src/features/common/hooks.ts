import { useNetworkState } from "expo-network";
import { useRouter } from "expo-router";
import { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import type { UserProfile } from "@/api/auth";
import { scopeOf, useSession } from "@/auth/session";
import { translateError, type TranslatableError } from "@/i18n";
import { DEFAULT_TIME_ZONE, type DateLanguage } from "@/lib/date";
import { toAppError } from "@/lib/errors";
import type { QueueScope } from "@/offline/types";
import { beginScan } from "@/scanner/scanBus";
import { scannedTextSchema } from "@/validation/schemas";

/**
 * Returns whether the device has a connection. Unknown counts as online, so that the first
 * request is attempted; a failed request is handled as a connectivity error anyway.
 */
export function useOnline(): boolean {
  const state = useNetworkState();
  return state.isConnected !== false && state.isInternetReachable !== false;
}

/** Data of the signed-in user needed by screens. */
export interface CurrentUser {
  user: UserProfile | null;
  scope: QueueScope | null;
  permissions: readonly string[];
  timeZone: string;
}

/** The signed-in user with queue scope, permissions and the tenant's time zone. */
export function useCurrentUser(): CurrentUser {
  const user = useSession((state) => state.user);
  return useMemo(
    () => ({ user, scope: scopeOf(user), permissions: user?.permissions ?? [], timeZone: user?.timeZone || DEFAULT_TIME_ZONE }),
    [user],
  );
}

/** Language used for dates. */
export function useDateLanguage(): DateLanguage {
  const { i18n } = useTranslation();
  return i18n.language === "en" ? "en" : "th";
}

/** Returns a function that turns any error into a message in the user's language. */
export function useErrorText(): (error: unknown) => string {
  const { t } = useTranslation();
  return useCallback(
    (error: unknown) => {
      const isStored = typeof error === "object" && error !== null && !(error instanceof Error) && ("code" in error || "detail" in error);
      return translateError(t, isStored ? (error as TranslatableError) : toAppError(error));
    },
    [t],
  );
}

/** Returns the value after it stopped changing for the delay; used for search fields. */
export function useDebounced<T>(value: T, delayMs = 400): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);
  return debounced;
}

/**
 * Returns a function that opens the scanner and resolves with the cleaned text, or `null` when
 * the user closed the scanner or the text was not usable.
 */
export function useScanner(): (mode?: "lot" | "location" | "any") => Promise<string | null> {
  const router = useRouter();
  return useCallback(
    async (mode = "any") => {
      const waiting = beginScan();
      router.push({ pathname: "/scanner", params: { mode } });
      const text = await waiting;
      if (text === null) {
        return null;
      }
      const parsed = scannedTextSchema.safeParse(text);
      return parsed.success ? parsed.data : null;
    },
    [router],
  );
}
