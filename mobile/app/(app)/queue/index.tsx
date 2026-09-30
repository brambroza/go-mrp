import { useMutation, useQuery } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import { useTranslation } from "react-i18next";
import { FlatList, RefreshControl, StyleSheet, View } from "react-native";

import { NoPermission, OfflineBanner, QueryError } from "@/features/common/components";
import { useCurrentUser, useDateLanguage, useErrorText, useOnline } from "@/features/common/hooks";
import { QUEUE_TONE } from "@/features/common/status";
import { canOpenTile } from "@/features/home/tiles";
import { formatDisplayDateTime } from "@/lib/date";
import { getQueueStore, syncQueue } from "@/offline/service";
import type { QueueItem } from "@/offline/types";
import { spacing } from "@/theme/tokens";
import { AppText, Badge, Banner, Button, Card, EmptyView, LoadingView, Screen } from "@/ui";

/** Order of the statuses in the list: problems first. */
const ORDER: Record<QueueItem["status"], number> = { failed: 0, sending: 1, pending: 2, sent: 3 };

/** Offline queue: documents waiting on this device, of the signed-in user only. */
export default function QueueScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const online = useOnline();
  const language = useDateLanguage();
  const errorText = useErrorText();
  const { permissions, scope, timeZone } = useCurrentUser();

  const items = useQuery({
    queryKey: ["queue", "list", scope?.tenantId, scope?.userId],
    queryFn: async () => {
      if (!scope) {
        return [];
      }
      const rows = await (await getQueueStore()).list(scope);
      return rows.sort((a, b) => ORDER[a.status] - ORDER[b.status] || b.createdAt.localeCompare(a.createdAt));
    },
    enabled: scope !== null,
  });

  const sync = useMutation({
    mutationFn: async () => {
      if (!scope) {
        throw new Error("No session");
      }
      return syncQueue(scope);
    },
    onSettled: () => void items.refetch(),
  });

  if (!canOpenTile(permissions, "queue")) {
    return (
      <Screen>
        <NoPermission />
      </Screen>
    );
  }
  if (items.isLoading) {
    return <LoadingView label={t("common.loading")} />;
  }

  const rows = items.data ?? [];
  const pendingCount = rows.filter((row) => row.status === "pending").length;
  const summary = sync.data;

  return (
    <Screen
      scroll={false}
      testID="queue-screen"
      footer={<Button label={sync.isPending ? t("queue.syncing") : t("queue.syncNow", { count: pendingCount })} onPress={() => sync.mutate()} loading={sync.isPending} disabled={!online || pendingCount === 0} testID="queue-sync" />}
    >
      <OfflineBanner />
      {items.error ? <QueryError error={items.error} onRetry={() => void items.refetch()} /> : null}
      {sync.error ? <Banner tone="danger" title={t("queue.syncFailed")} message={errorText(sync.error)} /> : null}
      {summary && !sync.isPending ? (
        <Banner
          tone={summary.failed > 0 ? "danger" : summary.waiting > 0 ? "warning" : "success"}
          title={t("queue.syncSummary", { sent: summary.sent, failed: summary.failed, waiting: summary.waiting })}
          message={summary.stoppedBy ? errorText(summary.stoppedBy) : undefined}
        />
      ) : null}
      <FlatList
        data={rows}
        keyExtractor={(row) => row.id}
        refreshControl={<RefreshControl refreshing={items.isRefetching} onRefresh={() => void items.refetch()} />}
        ListEmptyComponent={<EmptyView title={t("queue.emptyTitle")} message={t("queue.emptyMessage")} />}
        renderItem={({ item }) => {
          const waitingApproval = item.status === "sent" && item.serverStatus !== "Posted";
          return (
            <Card onPress={() => router.push({ pathname: "/queue/[id]", params: { id: item.id } })} accessibilityLabel={`${t(`queue.kind.${item.kind}`)} ${item.display.title}`} testID={`queue-item-${item.status}`}>
              <View style={styles.header}>
                <AppText variant="heading" style={styles.title}>
                  {t(`queue.kind.${item.kind}`)}
                </AppText>
                <Badge label={waitingApproval ? t("queue.status.approval") : t(`queue.status.${item.status}`)} tone={waitingApproval ? "warning" : QUEUE_TONE[item.status]} />
              </View>
              <AppText variant="bodyStrong">{item.display.title}</AppText>
              {item.serverDocumentNo ? <AppText>{t("queue.documentNo", { no: item.serverDocumentNo })}</AppText> : null}
              <AppText muted>{t("queue.meta", { lines: item.payload.lines.length, date: formatDisplayDateTime(item.createdAt, language, timeZone) })}</AppText>
              {item.status === "failed" || (item.status === "pending" && item.lastErrorCode) ? (
                <AppText accessibilityRole="alert">{t("queue.lastError", { message: errorText({ code: item.lastErrorCode, detail: item.lastErrorMessage, status: item.lastErrorStatus }) })}</AppText>
              ) : null}
            </Card>
          );
        }}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", gap: spacing.sm },
  title: { flexShrink: 1 },
});
