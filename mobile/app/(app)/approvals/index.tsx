import { useQuery } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import { useTranslation } from "react-i18next";
import { FlatList, RefreshControl, StyleSheet, View } from "react-native";

import { fetchApprovalInbox } from "@/api/approvals";
import { OnlineOnlyNotice, QueryError } from "@/features/common/components";
import { useCurrentUser, useDateLanguage, useOnline } from "@/features/common/hooks";
import { formatDisplayDateTime } from "@/lib/date";
import { formatMoney } from "@/lib/quantity";
import { spacing } from "@/theme/tokens";
import { AppText, Badge, Card, EmptyView, LoadingView, Screen } from "@/ui";

/** Approval inbox: requests waiting for the signed-in user. Works online only. */
export default function ApprovalInboxScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const online = useOnline();
  const language = useDateLanguage();
  const { scope, timeZone } = useCurrentUser();
  const inbox = useQuery({ queryKey: ["approvals", "inbox", scope?.tenantId, scope?.userId], queryFn: () => fetchApprovalInbox(1, 50), enabled: online });

  if (!online && !inbox.data) {
    return (
      <Screen>
        <OnlineOnlyNotice />
      </Screen>
    );
  }
  if (inbox.isLoading) {
    return <LoadingView label={t("common.loading")} />;
  }

  const rows = inbox.data?.items ?? [];
  return (
    <Screen scroll={false} testID="approvals-screen">
      {!online ? <OnlineOnlyNotice /> : null}
      {inbox.error ? <QueryError error={inbox.error} onRetry={() => void inbox.refetch()} /> : null}
      <FlatList
        data={rows}
        keyExtractor={(row) => row.id}
        refreshControl={<RefreshControl refreshing={inbox.isRefetching} onRefresh={() => void inbox.refetch()} />}
        ListEmptyComponent={inbox.error ? null : <EmptyView title={t("approvals.emptyTitle")} message={t("approvals.emptyMessage")} />}
        ListFooterComponent={
          inbox.data && inbox.data.total > rows.length ? (
            <AppText muted style={styles.more}>
              {t("approvals.more", { shown: rows.length, total: inbox.data.total })}
            </AppText>
          ) : null
        }
        renderItem={({ item }) => (
          <Card onPress={() => router.push({ pathname: "/approvals/[id]", params: { id: item.id } })} accessibilityLabel={`${item.documentNo} ${item.title}`} testID={`approval-${item.documentNo}`}>
            <View style={styles.header}>
              <AppText variant="heading">{item.documentNo}</AppText>
              <Badge label={t(`documentType.${item.documentType}`, { defaultValue: item.documentType })} tone="info" />
            </View>
            <AppText>{item.title}</AppText>
            {item.amount !== null ? <AppText variant="number">{t("common.baht", { amount: formatMoney(item.amount) })}</AppText> : null}
            <AppText muted>{t("approvals.requestedBy", { name: item.requestedByName, date: formatDisplayDateTime(item.requestedAt, language, timeZone) })}</AppText>
            <AppText muted>{t("approvals.step", { step: item.currentStepNo, total: item.totalSteps, name: item.currentStepName ?? "" })}</AppText>
          </Card>
        )}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  header: { flexDirection: "row", justifyContent: "space-between", alignItems: "center", gap: spacing.sm, marginBottom: spacing.xs },
  more: { textAlign: "center", marginVertical: spacing.md },
});
