import { useQuery } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { FlatList, RefreshControl, View } from "react-native";

import { fetchReceivableOrders } from "@/api/purchasing";
import { NoPermission, OnlineOnlyNotice, QueryError } from "@/features/common/components";
import { useCurrentUser, useDateLanguage, useDebounced, useOnline } from "@/features/common/hooks";
import { canOpenTile } from "@/features/home/tiles";
import { formatDisplayDate } from "@/lib/date";
import { AppText, Badge, Card, EmptyView, LoadingView, Screen, TextField } from "@/ui";

/** Goods receipt, step 1: pick a purchase order that can still be received. */
export default function ReceivePickOrderScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const online = useOnline();
  const language = useDateLanguage();
  const { permissions, scope } = useCurrentUser();
  const [search, setSearch] = useState("");
  const term = useDebounced(search.trim(), 400);
  const orders = useQuery({ queryKey: ["receivable-orders", scope?.tenantId, term], queryFn: () => fetchReceivableOrders(term), enabled: online });

  if (!canOpenTile(permissions, "receive")) {
    return (
      <Screen>
        <NoPermission />
      </Screen>
    );
  }

  return (
    <Screen scroll={false} testID="receive-orders-screen">
      {!online ? <OnlineOnlyNotice /> : null}
      <TextField
        label={t("receive.searchOrder")}
        value={search}
        onChangeText={setSearch}
        autoCapitalize="characters"
        autoCorrect={false}
        maxLength={40}
        returnKeyType="search"
        hint={t("receive.searchHint")}
        testID="receive-search"
      />
      {orders.isLoading ? <LoadingView label={t("common.loading")} /> : null}
      {orders.error ? <QueryError error={orders.error} onRetry={() => void orders.refetch()} /> : null}
      {!orders.isLoading ? (
        <FlatList
          data={orders.data ?? []}
          keyExtractor={(row) => row.id}
          keyboardShouldPersistTaps="handled"
          refreshControl={<RefreshControl refreshing={orders.isRefetching} onRefresh={() => void orders.refetch()} />}
          ListEmptyComponent={orders.error || !online ? null : <EmptyView title={t("receive.emptyTitle")} message={t("receive.emptyMessage")} />}
          renderItem={({ item }) => (
            <Card onPress={() => router.push({ pathname: "/receive/[poId]", params: { poId: item.id } })} accessibilityLabel={`${item.documentNo} ${item.supplierName}`} testID={`po-${item.documentNo}`}>
              <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center", gap: 8 }}>
                <AppText variant="heading">{item.documentNo}</AppText>
                <Badge label={t(`poStatus.${item.status}`, { defaultValue: item.status })} tone={item.status === "PartiallyReceived" ? "warning" : "info"} />
              </View>
              <AppText>{item.supplierName}</AppText>
              <AppText muted>{t("receive.orderMeta", { lines: item.lineCount, date: formatDisplayDate(item.deliveryDate ?? item.documentDate, language) })}</AppText>
            </Card>
          )}
        />
      ) : null}
    </Screen>
  );
}
