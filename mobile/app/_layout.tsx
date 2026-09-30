import { Sarabun_400Regular } from "@expo-google-fonts/sarabun/400Regular";
import { Sarabun_500Medium } from "@expo-google-fonts/sarabun/500Medium";
import { Sarabun_700Bold } from "@expo-google-fonts/sarabun/700Bold";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useFonts } from "expo-font";
import { Stack } from "expo-router";
import * as SplashScreen from "expo-splash-screen";
import { StatusBar } from "expo-status-bar";
import { useEffect, useState } from "react";
import { SafeAreaProvider } from "react-native-safe-area-context";

import { onSignedOut, restoreSession, useSession } from "@/auth/session";
import { initI18n, loadStoredLanguage } from "@/i18n";
import { AppError } from "@/lib/errors";
import { ThemeProvider, useTheme } from "@/theme/ThemeProvider";

initI18n();
void SplashScreen.preventAutoHideAsync().catch(() => undefined);

/** Server-state cache. Business errors (4xx) are not retried; connectivity errors are retried once. */
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: (failureCount, error) => failureCount < 1 && error instanceof AppError && (error.kind === "network" || error.kind === "timeout"),
    },
    mutations: { retry: false },
  },
});

// Data of the previous user must not be visible to the next one on a shared device.
onSignedOut(() => queryClient.clear());

/** Navigation stack; screens of the app are reachable only while signed in. */
function RootNavigator() {
  const { colors, dark } = useTheme();
  const phase = useSession((state) => state.phase);
  const signedIn = phase === "signedIn";

  return (
    <>
      <StatusBar style={dark ? "light" : "dark"} />
      <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.background } }}>
        <Stack.Protected guard={signedIn}>
          <Stack.Screen name="(app)" />
        </Stack.Protected>
        <Stack.Protected guard={!signedIn}>
          <Stack.Screen name="login" />
        </Stack.Protected>
      </Stack>
    </>
  );
}

/** Root layout: loads the Thai font, the language and the session, then shows the app. */
export default function RootLayout() {
  const [fontsLoaded, fontError] = useFonts({ Sarabun_400Regular, Sarabun_500Medium, Sarabun_700Bold });
  const [prepared, setPrepared] = useState(false);
  const phase = useSession((state) => state.phase);

  useEffect(() => {
    let active = true;
    void Promise.all([loadStoredLanguage(), restoreSession()]).finally(() => {
      if (active) {
        setPrepared(true);
      }
    });
    return () => {
      active = false;
    };
  }, []);

  const ready = (fontsLoaded || fontError !== null) && prepared && phase !== "restoring";

  useEffect(() => {
    if (ready) {
      void SplashScreen.hideAsync().catch(() => undefined);
    }
  }, [ready]);

  if (!ready) {
    return null;
  }

  return (
    <SafeAreaProvider>
      <QueryClientProvider client={queryClient}>
        <ThemeProvider>
          <RootNavigator />
        </ThemeProvider>
      </QueryClientProvider>
    </SafeAreaProvider>
  );
}
