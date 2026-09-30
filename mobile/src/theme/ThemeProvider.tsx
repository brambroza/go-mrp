import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { useColorScheme } from "react-native";

import { getDeviceKeyValueStorage } from "@/storage/keyValue";

import { darkColors, lightColors, type Theme } from "./tokens";

/** Theme chosen by the user. `system` follows the device. */
export type ThemePreference = "system" | "light" | "dark";

const STORAGE_KEY = "app.theme";

interface ThemeContextValue {
  theme: Theme;
  preference: ThemePreference;
  setPreference: (preference: ThemePreference) => void;
}

const ThemeContext = createContext<ThemeContextValue | null>(null);

/** Returns whether the text is a theme preference. */
function isPreference(value: string | null): value is ThemePreference {
  return value === "system" || value === "light" || value === "dark";
}

/** Provides the light or dark theme and remembers the user's choice on the device. */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const system = useColorScheme();
  const [preference, setPreferenceState] = useState<ThemePreference>("system");

  useEffect(() => {
    let active = true;
    getDeviceKeyValueStorage()
      .get(STORAGE_KEY)
      .then((stored) => {
        if (active && isPreference(stored)) {
          setPreferenceState(stored);
        }
      })
      .catch(() => undefined);
    return () => {
      active = false;
    };
  }, []);

  const setPreference = useCallback((next: ThemePreference) => {
    setPreferenceState(next);
    getDeviceKeyValueStorage()
      .set(STORAGE_KEY, next)
      .catch(() => undefined);
  }, []);

  const value = useMemo<ThemeContextValue>(() => {
    const dark = preference === "system" ? system === "dark" : preference === "dark";
    return { theme: { dark, colors: dark ? darkColors : lightColors }, preference, setPreference };
  }, [preference, setPreference, system]);

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

/** Current theme. */
export function useTheme(): Theme {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error("useTheme must be used inside ThemeProvider");
  }
  return context.theme;
}

/** Theme preference of the user and its setter. */
export function useThemePreference(): Pick<ThemeContextValue, "preference" | "setPreference"> {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error("useThemePreference must be used inside ThemeProvider");
  }
  return { preference: context.preference, setPreference: context.setPreference };
}
