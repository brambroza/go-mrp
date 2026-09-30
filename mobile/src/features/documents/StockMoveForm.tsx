import { useQuery } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { StyleSheet, View } from "react-native";

import { fetchAllocationPreview, fetchLotByNumber, type SaveStockDocumentLine } from "@/api/inventory";
import type { Item, Location, Warehouse } from "@/api/masters";
import { ItemSearchButton, LocationField, OfflineBanner, WarehouseField } from "@/features/common/components";
import { useCurrentUser, useDateLanguage, useDebounced, useErrorText, useOnline, useScanner } from "@/features/common/hooks";
import { QC_TONE } from "@/features/common/status";
import { formatDisplayDate, todayIso } from "@/lib/date";
import { AppError } from "@/lib/errors";
import { formatQuantity } from "@/lib/quantity";
import { newId } from "@/lib/uuid";
import { MAX_USER_REMARK } from "@/offline/sender";
import { spacing } from "@/theme/tokens";
import { AppText, Badge, Banner, Button, Card, ConfirmDialog, Field, QuantityField, Screen, SectionTitle, TextField } from "@/ui";

import { readQuantity, textOrNull } from "./lines";
import { SubmitResult, useSubmitDocument } from "./submit";

/** Kind of stock movement handled by the form. */
export type StockMoveMode = "issue" | "transfer";

/** Line of the form. */
interface MoveLine {
  key: string;
  itemId: string;
  itemCode: string;
  itemName: string;
  unitCode: string;
  /** Lot chosen by scanning; `null` lets the system pick lots (FIFO/FEFO). */
  lotId: string | null;
  lotNo: string | null;
  lotQc: "Quarantine" | "Released" | "Rejected" | "OnHold" | null;
  quantity: string;
  location: Location | null;
  toLocation: Location | null;
}

/** Longest list of lines accepted by the API. */
const MAX_LINES = 500;

/** FIFO/FEFO suggestion of one line, with a warning when the scanned lot is not the first one. */
function AllocationHint({ line, warehouseId, quantity }: { line: MoveLine; warehouseId: string; quantity: number | null }) {
  const { t } = useTranslation();
  const online = useOnline();
  const language = useDateLanguage();
  const errorText = useErrorText();
  const wanted = useDebounced(quantity ?? 1, 500);
  const preview = useQuery({
    queryKey: ["allocation-preview", line.itemId, warehouseId, wanted],
    queryFn: () => fetchAllocationPreview(line.itemId, warehouseId, wanted),
    enabled: online,
    staleTime: 0,
  });

  if (!online) {
    return <AppText muted>{t("issue.fifoOffline")}</AppText>;
  }
  if (preview.isLoading) {
    return <AppText muted>{t("issue.fifoLoading")}</AppText>;
  }
  if (preview.error || !preview.data) {
    return <AppText muted>{t("issue.fifoFailed", { message: errorText(preview.error) })}</AppText>;
  }

  const lots = preview.data.lots;
  const first = lots[0];
  const notFirst = line.lotId !== null && first !== undefined && first.lotId !== line.lotId;
  return (
    <View style={styles.hint}>
      {notFirst ? <Banner tone="warning" title={t("issue.notFifoTitle")} message={t("issue.notFifoMessage", { lotNo: first.lotNo, location: first.locationCode ?? "-" })} testID="fifo-warning" /> : null}
      {line.lotId !== null && !lots.some((lot) => lot.lotId === line.lotId) && first === undefined ? <Banner tone="warning" title={t("issue.noAvailableTitle")} message={t("issue.noAvailableMessage")} /> : null}
      {!preview.data.sufficient && quantity !== null ? (
        <Banner tone="warning" title={t("issue.insufficientTitle")} message={t("issue.insufficientMessage", { available: formatQuantity(preview.data.available), unit: line.unitCode })} />
      ) : null}
      {lots.length > 0 ? (
        <>
          <AppText variant="label" muted>
            {t("issue.fifoTitle")}
          </AppText>
          {lots.slice(0, 3).map((lot, index) => (
            <AppText key={`${lot.lotId}-${lot.locationId ?? "none"}`} variant={lot.lotId === line.lotId ? "bodyStrong" : "body"}>
              {t("issue.fifoRow", {
                order: index + 1,
                lotNo: lot.lotNo,
                location: lot.locationCode ?? "-",
                available: formatQuantity(lot.available),
                expiry: formatDisplayDate(lot.expiryDate, language) || "-",
              })}
            </AppText>
          ))}
        </>
      ) : null}
    </View>
  );
}

/**
 * Form shared by goods issue and transfer: pick the warehouse, add lines by searching an item or
 * scanning a lot, review, submit. The API enforces availability; the form only warns.
 */
export function StockMoveForm({ mode }: { mode: StockMoveMode }) {
  const { t } = useTranslation();
  const router = useRouter();
  const online = useOnline();
  const language = useDateLanguage();
  const errorText = useErrorText();
  const scan = useScanner();
  const { timeZone } = useCurrentUser();
  const controller = useSubmitDocument();

  const [warehouse, setWarehouse] = useState<Warehouse | null>(null);
  const [toWarehouse, setToWarehouse] = useState<Warehouse | null>(null);
  const [lines, setLines] = useState<MoveLine[]>([]);
  const [remark, setRemark] = useState("");
  const [step, setStep] = useState<"edit" | "review">("edit");
  const [showErrors, setShowErrors] = useState(false);
  const [scanMessage, setScanMessage] = useState<{ tone: "danger" | "warning"; text: string } | null>(null);
  const [scanning, setScanning] = useState(false);
  const [removing, setRemoving] = useState<MoveLine | null>(null);

  const change = (key: string, patch: Partial<MoveLine>) => setLines((current) => current.map((line) => (line.key === key ? { ...line, ...patch } : line)));

  const addItem = (item: Item) => {
    if (lines.length >= MAX_LINES) {
      setScanMessage({ tone: "danger", text: t("validation.too_many_lines") });
      return;
    }
    setScanMessage(null);
    setLines((current) => [
      ...current,
      { key: newId(), itemId: item.id, itemCode: item.code, itemName: item.name, unitCode: item.stockUnitCode, lotId: null, lotNo: null, lotQc: null, quantity: "", location: null, toLocation: null },
    ]);
  };

  const addLot = async () => {
    setScanMessage(null);
    const text = await scan("lot");
    if (text === null) {
      return;
    }
    if (lines.some((line) => line.lotNo?.toLowerCase() === text.toLowerCase())) {
      setScanMessage({ tone: "warning", text: t("issue.lotAlreadyAdded", { lotNo: text }) });
      return;
    }
    if (lines.length >= MAX_LINES) {
      setScanMessage({ tone: "danger", text: t("validation.too_many_lines") });
      return;
    }
    setScanning(true);
    try {
      const lot = await fetchLotByNumber(text);
      setLines((current) => [
        ...current,
        { key: newId(), itemId: lot.itemId, itemCode: lot.itemCode, itemName: lot.itemName, unitCode: "", lotId: lot.id, lotNo: lot.lotNo, lotQc: lot.qcStatus, quantity: "", location: null, toLocation: null },
      ]);
    } catch (failure) {
      const notFound = failure instanceof AppError && failure.status === 404;
      setScanMessage({ tone: "danger", text: notFound ? t("lot.notFoundMessage", { lotNo: text }) : errorText(failure) });
    } finally {
      setScanning(false);
    }
  };

  const destination = mode === "transfer" ? toWarehouse : null;
  const checked = lines.map((line) => {
    const quantity = readQuantity(line.quantity, { required: true });
    const samePlace = mode === "transfer" && warehouse !== null && destination !== null && warehouse.id === destination.id && (line.location?.id ?? null) === (line.toLocation?.id ?? null);
    return { line, quantity, samePlace };
  });
  const remarkTooLong = remark.trim().length > MAX_USER_REMARK;
  const ready =
    warehouse !== null && (mode === "issue" || destination !== null) && lines.length > 0 && !remarkTooLong && checked.every((row) => row.quantity.error === null && !row.samePlace);

  if (controller.outcome) {
    return (
      <Screen testID={`${mode}-result`}>
        <SubmitResult
          outcome={controller.outcome}
          onDone={() => router.dismissTo("/")}
          onNew={() => {
            controller.reset();
            setLines([]);
            setRemark("");
            setStep("edit");
            setShowErrors(false);
          }}
          onEdit={() => {
            controller.reset();
            setStep("edit");
          }}
        />
      </Screen>
    );
  }

  const submit = () => {
    if (!warehouse || !ready) {
      return;
    }
    const payloadLines: SaveStockDocumentLine[] = checked.map(({ line, quantity }) => ({
      itemId: line.itemId,
      quantity: quantity.value ?? 0,
      lotId: line.lotId,
      locationId: line.location?.id ?? null,
      toLocationId: mode === "transfer" ? (line.toLocation?.id ?? null) : null,
    }));
    void controller.submit({
      kind: mode,
      payload: {
        documentType: mode === "issue" ? "Issue" : "Transfer",
        documentDate: todayIso(timeZone),
        warehouseId: warehouse.id,
        toWarehouseId: destination?.id ?? null,
        remark: textOrNull(remark),
        lines: payloadLines,
      },
      display: {
        title: destination ? `${warehouse.code} → ${destination.code}` : warehouse.code,
        lines: lines.map((line) => ({ itemCode: line.itemCode, itemName: line.itemName, unitCode: line.unitCode || undefined, lotNo: line.lotNo ?? undefined })),
      },
    });
  };

  if (step === "review" && warehouse) {
    return (
      <Screen
        testID={`${mode}-review`}
        footer={
          <>
            <Button label={online ? t("submit.confirm") : t("submit.saveToQueue")} onPress={submit} loading={controller.submitting} testID={`${mode}-submit`} />
            <Button label={t("common.edit")} variant="secondary" onPress={() => setStep("edit")} disabled={controller.submitting} />
          </>
        }
      >
        <OfflineBanner />
        {controller.error ? <Banner tone="danger" title={t("submit.failedTitle")} message={errorText(controller.error)} /> : null}
        <SectionTitle>{t("submit.reviewTitle")}</SectionTitle>
        <Card>
          <Field label={mode === "transfer" ? t("transfer.fromWarehouse") : t("warehouse.label")} value={`${warehouse.code} · ${warehouse.name}`} />
          {destination ? <Field label={t("transfer.toWarehouse")} value={`${destination.code} · ${destination.name}`} /> : null}
          <Field label={t("document.date")} value={formatDisplayDate(todayIso(timeZone), language)} />
          {remark.trim() ? <Field label={t("document.remark")} value={remark.trim()} /> : null}
        </Card>
        {checked.map(({ line, quantity }, index) => (
          <Card key={line.key}>
            <AppText variant="bodyStrong">{`${index + 1}. ${line.itemCode}`}</AppText>
            <AppText muted>{line.itemName}</AppText>
            <Field label={t("document.quantity")} value={`${formatQuantity(quantity.value)} ${line.unitCode}`.trim()} strong />
            <Field label={t("lot.label")} value={line.lotNo ?? t("issue.lotBySystem")} />
            <Field label={mode === "transfer" ? t("transfer.fromLocation") : t("location.label")} value={line.location?.code ?? "-"} />
            {mode === "transfer" ? <Field label={t("transfer.toLocation")} value={line.toLocation?.code ?? "-"} /> : null}
          </Card>
        ))}
      </Screen>
    );
  }

  return (
    <Screen
      testID={`${mode}-edit`}
      footer={
        <Button
          label={t("submit.review", { count: lines.length })}
          onPress={() => {
            setShowErrors(true);
            if (ready) {
              setStep("review");
            }
          }}
          testID={`${mode}-to-review`}
        />
      }
    >
      <OfflineBanner />
      <WarehouseField
        label={mode === "transfer" ? t("transfer.fromWarehouse") : t("issue.warehouse")}
        value={warehouse}
        onChange={(next) => {
          setWarehouse(next);
          setLines((current) => current.map((line) => ({ ...line, location: null })));
        }}
        error={showErrors && !warehouse ? t("validation.warehouse_required") : null}
      />
      {mode === "transfer" ? (
        <WarehouseField
          label={t("transfer.toWarehouse")}
          value={toWarehouse}
          onChange={(next) => {
            setToWarehouse(next);
            setLines((current) => current.map((line) => ({ ...line, toLocation: null })));
          }}
          error={showErrors && !toWarehouse ? t("validation.to_warehouse_required") : null}
        />
      ) : null}

      <SectionTitle>{t("document.lines")}</SectionTitle>
      {scanMessage ? <Banner tone={scanMessage.tone} title={scanMessage.text} testID="scan-message" /> : null}
      {showErrors && lines.length === 0 ? <Banner tone="danger" title={t("validation.lines_required")} /> : null}
      {!warehouse ? <AppText muted>{t("issue.pickWarehouseFirst")}</AppText> : null}

      {checked.map(({ line, quantity, samePlace }, index) => (
        <Card key={line.key} testID={`${mode}-line-${index + 1}`}>
          <AppText variant="bodyStrong">{`${index + 1}. ${line.itemCode}`}</AppText>
          <AppText muted>{line.itemName}</AppText>
          {line.lotNo ? (
            <View style={styles.lot}>
              <AppText variant="bodyStrong">{t("issue.lotScanned", { lotNo: line.lotNo })}</AppText>
              {line.lotQc ? <Badge label={t(`qc.${line.lotQc}`)} tone={QC_TONE[line.lotQc]} /> : null}
            </View>
          ) : (
            <AppText muted>{t("issue.lotBySystem")}</AppText>
          )}
          {line.lotQc !== null && line.lotQc !== "Released" ? <Banner tone="warning" title={t("issue.lotNotReleasedTitle")} message={t("lot.notReleased")} /> : null}
          <QuantityField
            label={t("document.quantity")}
            unit={line.unitCode || undefined}
            value={line.quantity}
            onChangeText={(text) => change(line.key, { quantity: text })}
            errorCode={showErrors || line.quantity !== "" ? quantity.error : null}
          />
          {warehouse ? <AllocationHint line={line} warehouseId={warehouse.id} quantity={quantity.value} /> : null}
          <LocationField
            label={mode === "transfer" ? t("transfer.fromLocation") : t("location.label")}
            value={line.location}
            onChange={(location) => change(line.key, { location })}
            warehouseId={warehouse?.id ?? null}
          />
          {mode === "transfer" ? (
            <LocationField
              label={t("transfer.toLocation")}
              value={line.toLocation}
              onChange={(location) => change(line.key, { toLocation: location })}
              warehouseId={destination?.id ?? null}
              error={samePlace ? t("validation.same_place") : null}
            />
          ) : null}
          <Button label={t("document.removeLine")} variant="ghost" onPress={() => setRemoving(line)} />
        </Card>
      ))}

      <View style={styles.add}>
        <Button label={scanning ? t("common.loading") : t("issue.scanLot")} onPress={() => void addLot()} loading={scanning} disabled={!warehouse || !online} testID={`${mode}-scan-lot`} />
        {warehouse ? <ItemSearchButton label={t("issue.searchItem")} onPick={addItem} /> : null}
        {!online ? <AppText muted>{t("issue.addNeedsOnline")}</AppText> : null}
      </View>

      <TextField
        label={t("document.remarkOptional")}
        value={remark}
        onChangeText={setRemark}
        error={remarkTooLong ? t("validation.too_long") : null}
        multiline
        maxLength={MAX_USER_REMARK}
      />

      <ConfirmDialog
        visible={removing !== null}
        title={t("document.removeLineTitle")}
        message={removing ? `${removing.itemCode} ${removing.lotNo ?? ""}`.trim() : undefined}
        confirmLabel={t("document.removeLine")}
        cancelLabel={t("common.cancel")}
        destructive
        onConfirm={() => {
          setLines((current) => current.filter((line) => line.key !== removing?.key));
          setRemoving(null);
        }}
        onCancel={() => setRemoving(null)}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  hint: { marginBottom: spacing.md, gap: spacing.xs },
  lot: { flexDirection: "row", alignItems: "center", gap: spacing.sm, flexWrap: "wrap" },
  add: { gap: spacing.sm, marginVertical: spacing.md },
});
