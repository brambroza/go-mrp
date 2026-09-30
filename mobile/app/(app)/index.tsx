import { useQuery } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import { useTranslation } from "react-i18next";
import { Pressable, StyleSheet, View } from "react-native";

import { useSession } from "@/auth/session";
import { OfflineBanner } from "@/features/common/components";
import { useCurrentUser } from "@/features/common/hooks";
import { visibleTiles } from "@/features/home/tiles";
import { getQueueStore } from "@/offline/service";
import { useTheme } from "@/theme/ThemeProvider";
import { radius, spacing } from "@/theme/tokens";
import { AppText, Banner, Button, Screen } from "@/ui";

/** Home screen: one large tile per job the user is allowed to do. */
export default function HomeScreen() {
  const { t } = useTranslation();
  const { colors } = useTheme();
  const router = useRouter();
  const { user, scope, permissions } = useCurrentUser();
  const restoredOffline = useSession((state) => state.restoredOffline);
  const tiles = visibleTiles(permissions);

  const waiting = useQuery({
    queryKey: ["queue", "badge", scope?.tenantId, scope?.userId],
    queryFn: async () => {
      if (!scope) {
        return { open: 0, failed: 0 };
      }
      const store = await getQueueStore();
      return { open: await store.count(scope, ["pending", "sending", "failed"]), failed: await store.count(scope, ["failed"]) };
    },
    enabled: scope !== null,
  });
  const openCount = waiting.data?.open ?? 0;
  const failedCount = waiting.data?.failed ?? 0;

  return (
    <Screen testID="home-screen">
      <OfflineBanner />
      {restoredOffline ? <Banner tone="info" title={t("home.offlineSessionTitle")} message={t("home.offlineSessionMessage")} /> : null}
      <AppText variant="title" accessibilityRole="header">
        {t("home.greeting", { name: user?.displayName ?? "" })}
      </AppText>
      <AppText muted style={styles.tenant}>
        {user?.tenantName ?? ""}
      </AppText>

      {failedCount > 0 ? (
        <Banner tone="danger" title={t("home.failedTitle", { count: failedCount })} message={t("home.failedMessage")} actionLabel={t("home.openQueue")} onAction={() => router.push("/queue")} />
      ) : null}

      <View style={styles.grid}>
        {tiles.map((tile) => {
          const badge = tile.key === "queue" && openCount > 0 ? openCount : 0;
          const title = t(tile.titleKey);
          return (
            <Pressable
              key={tile.key}
              accessibilityRole="button"
              accessibilityLabel={badge > 0 ? t("home.tileWithBadge", { title, count: badge }) : title}
              onPress={() => router.push(tile.route)}
              testID={`tile-${tile.key}`}
              style={({ pressed }) => [styles.tile, { backgroundColor: colors.surface, borderColor: colors.border, opacity: pressed ? 0.75 : 1 }]}
            >
              <AppText variant="title" color={colors.primary} style={styles.symbol}>
                {tile.symbol}
              </AppText>
              <AppText variant="bodyStrong" style={styles.tileTitle} numberOfLines={2}>
                {title}
              </AppText>
              {badge > 0 ? (
                <View style={[styles.badge, { backgroundColor: failedCount > 0 ? colors.danger : colors.primary }]}>
                  <AppText variant="label" color={failedCount > 0 ? colors.onDanger : colors.onPrimary}>
                    {badge > 99 ? "99+" : String(badge)}
                  </AppText>
                </View>
              ) : null}
            </Pressable>
          );
        })}
      </View>

      <Button label={t("settings.title")} variant="secondary" onPress={() => router.push("/settings")} style={styles.settings} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  tenant: { marginBottom: spacing.lg },
  grid: { flexDirection: "row", flexWrap: "wrap", gap: spacing.md },
  tile: {
    width: "47%",
    flexGrow: 1,
    minHeight: 132,
    borderWidth: 1,
    borderRadius: radius.lg,
    padding: spacing.lg,
    justifyContent: "center",
    alignItems: "center",
  },
  symbol: { fontSize: 36, lineHeight: 44 },
  tileTitle: { textAlign: "center", marginTop: spacing.xs },
  badge: { position: "absolute", top: spacing.sm, right: spacing.sm, minWidth: 32, height: 32, borderRadius: 16, paddingHorizontal: spacing.sm, alignItems: "center", justifyContent: "center" },
  settings: { marginTop: spacing.xl },
});
