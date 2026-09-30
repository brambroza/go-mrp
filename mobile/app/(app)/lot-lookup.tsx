import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";

import { fetchLotByNumber, fetchOnHandOfLot } from "@/api/inventory";
import { NoPermission, OfflineBanner, QueryError } from "@/features/common/components";
import { useCurrentUser, useDateLanguage, useOnline, useScanner } from "@/features/common/hooks";
import { QC_TONE } from "@/features/common/status";
import { canOpenTile } from "@/features/home/tiles";
import { compareIsoDates, formatDisplayDate, formatDisplayDateTime, todayIso } from "@/lib/date";
import { AppError } from "@/lib/errors";
import { formatQuantity } from "@/lib/quantity";
import { AppText, Badge, Banner, Button, Card, Field, LoadingView, Screen, SectionTitle, TextField } from "@/ui";
import { scannedTextSchema } from "@/validation/schemas";

/** Lot lookup: scan or type a lot number and see item, on-hand, QC status and expiry. */
export default function LotLookupScreen() {
  const { t } = useTranslation();
  const online = useOnline();
  const language = useDateLanguage();
  const scan = useScanner();
  const { permissions, timeZone } = useCurrentUser();
  const [text, setText] = useState("");
  const [lotNo, setLotNo] = useState<string | null>(null);
  const [inputError, setInputError] = useState<string | null>(null);

  const lot = useQuery({ queryKey: ["lot", lotNo], queryFn: () => fetchLotByNumber(lotNo ?? ""), enabled: lotNo !== null && online, staleTime: 0 });
  const onHand = useQuery({ queryKey: ["lot-on-hand", lot.data?.id], queryFn: () => fetchOnHandOfLot(lot.data?.id ?? ""), enabled: lot.data !== undefined && online, staleTime: 0 });

  if (!canOpenTile(permissions, "lotLookup")) {
    return (
      <Screen>
        <NoPermission />
      </Screen>
    );
  }

  const search = (value: string) => {
    const parsed = scannedTextSchema.safeParse(value);
    if (!parsed.success) {
      setInputError(t(parsed.error.issues[0]?.message ?? "validation.invalid"));
      return;
    }
    setInputError(null);
    setText(parsed.data);
    setLotNo(parsed.data);
  };

  const scanLot = async () => {
    const scanned = await scan("lot");
    if (scanned !== null) {
      search(scanned);
    }
  };

  const notFound = lot.error instanceof AppError && lot.error.status === 404;
  const expired = lot.data?.expiryDate ? compareIsoDates(lot.data.expiryDate, todayIso(timeZone)) < 0 : false;

  return (
    <Screen testID="lot-lookup-screen">
      <OfflineBanner />
      <Button label={t("lot.scan")} onPress={() => void scanLot()} disabled={!online} testID="lot-scan" />
      <TextField
        label={t("lot.number")}
        value={text}
        onChangeText={(value) => {
          setText(value);
          setInputError(null);
        }}
        error={inputError}
        autoCapitalize="characters"
        autoCorrect={false}
        maxLength={200}
        returnKeyType="search"
        onSubmitEditing={() => search(text)}
        trailing={<Button label={t("common.search")} variant="secondary" onPress={() => search(text)} disabled={!online} />}
        testID="lot-input"
      />

      {lot.isFetching ? <LoadingView label={t("common.loading")} /> : null}
      {!lot.isFetching && notFound ? <Banner tone="warning" title={t("lot.notFoundTitle")} message={t("lot.notFoundMessage", { lotNo: lotNo ?? "" })} testID="lot-not-found" /> : null}
      {!lot.isFetching && lot.error && !notFound ? <QueryError error={lot.error} onRetry={() => void lot.refetch()} /> : null}

      {!lot.isFetching && lot.data ? (
        <>
          <Card testID="lot-detail">
            <AppText variant="title">{lot.data.lotNo}</AppText>
            <Badge label={t(`qc.${lot.data.qcStatus}`)} tone={QC_TONE[lot.data.qcStatus]} />
            {lot.data.qcStatus !== "Released" ? <AppText style={{ marginTop: 8 }}>{t("lot.notReleased")}</AppText> : null}
            <Field label={t("item.label")} value={`${lot.data.itemCode}\n${lot.data.itemName}`} />
            <Field label={t("lot.onHand")} value={formatQuantity(lot.data.onHand)} strong />
            <Field label={t("lot.expiry")} value={formatDisplayDate(lot.data.expiryDate, language) || "-"} />
            <Field label={t("lot.mfg")} value={formatDisplayDate(lot.data.mfgDate, language) || "-"} />
            <Field label={t("lot.supplierLot")} value={lot.data.supplierLot ?? "-"} />
            <Field label={t("lot.receivedAt")} value={formatDisplayDateTime(lot.data.receivedAt, language, timeZone)} />
            {lot.data.qcRemark ? <Field label={t("lot.qcRemark")} value={lot.data.qcRemark} /> : null}
          </Card>
          {expired ? <Banner tone="danger" title={t("lot.expiredTitle")} message={t("lot.expiredMessage")} /> : null}

          <SectionTitle>{t("lot.whereTitle")}</SectionTitle>
          {onHand.isLoading ? <LoadingView label={t("common.loading")} /> : null}
          {onHand.error ? <QueryError error={onHand.error} onRetry={() => void onHand.refetch()} /> : null}
          {onHand.data?.length === 0 ? <AppText muted>{t("lot.noStock")}</AppText> : null}
          {onHand.data?.map((row) => (
            <Card key={`${row.warehouseId}-${row.locationId ?? "none"}`}>
              <Field label={t("warehouse.label")} value={row.warehouseCode ?? "-"} />
              <Field label={t("location.label")} value={row.locationCode ?? "-"} />
              <Field label={t("lot.onHand")} value={`${formatQuantity(row.quantity)} ${row.unitCode ?? ""}`.trim()} strong />
            </Card>
          ))}
        </>
      ) : null}
    </Screen>
  );
}
