import { useQueryClient } from "@tanstack/react-query";
import { Stack } from "expo-router";
import { useEffect } from "react";
import { useTranslation } from "react-i18next";
import { AppState } from "react-native";

import { useCurrentUser, useOnline } from "@/features/common/hooks";
import { onQueueChanged, syncQueue } from "@/offline/service";
import { useTheme } from "@/theme/ThemeProvider";
import { fonts } from "@/theme/tokens";

/**
 * Layout of the signed-in area. Starts a queue sync when the app opens, returns to the
 * foreground or regains its connection.
 */
export default function AppLayout() {
  const { t } = useTranslation();
  const { colors } = useTheme();
  const { scope } = useCurrentUser();
  const online = useOnline();
  const queryClient = useQueryClient();
  const tenantId = scope?.tenantId;
  const userId = scope?.userId;

  useEffect(() => onQueueChanged(() => void queryClient.invalidateQueries({ queryKey: ["queue"] })), [queryClient]);

  useEffect(() => {
    if (!tenantId || !userId || !online) {
      return;
    }
    const run = () => void syncQueue({ tenantId, userId }).catch(() => undefined);
    run();
    const subscription = AppState.addEventListener("change", (state) => {
      if (state === "active") {
        run();
      }
    });
    return () => subscription.remove();
  }, [online, tenantId, userId]);

  return (
    <Stack
      screenOptions={{
        headerStyle: { backgroundColor: colors.surface },
        headerTintColor: colors.text,
        headerTitleStyle: { fontFamily: fonts.bold, fontSize: 20 },
        headerBackTitle: t("common.back"),
        contentStyle: { backgroundColor: colors.background },
      }}
    >
      <Stack.Screen name="index" options={{ title: t("home.title") }} />
      <Stack.Screen name="settings" options={{ title: t("settings.title") }} />
      <Stack.Screen name="approvals/index" options={{ title: t("approvals.title") }} />
      <Stack.Screen name="approvals/[id]" options={{ title: t("approvals.detailTitle") }} />
      <Stack.Screen name="lot-lookup" options={{ title: t("lot.title") }} />
      <Stack.Screen name="receive/index" options={{ title: t("receive.title") }} />
      <Stack.Screen name="receive/[poId]" options={{ title: t("receive.title") }} />
      <Stack.Screen name="issue" options={{ title: t("issue.title") }} />
      <Stack.Screen name="transfer" options={{ title: t("transfer.title") }} />
      <Stack.Screen name="count" options={{ title: t("count.title") }} />
      <Stack.Screen name="queue/index" options={{ title: t("queue.title") }} />
      <Stack.Screen name="queue/[id]" options={{ title: t("queue.detailTitle") }} />
      <Stack.Screen name="scanner" options={{ title: t("scanner.title"), presentation: "fullScreenModal", headerShown: false }} />
    </Stack>
  );
}
