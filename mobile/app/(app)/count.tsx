import { useMutation } from "@tanstack/react-query";
import { useRouter } from "expo-router";
import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { FlatList, StyleSheet, View } from "react-native";

import { createCountSheet, type SaveStockDocumentLine, type StockDocument } from "@/api/inventory";
import type { Location, Warehouse } from "@/api/masters";
import { LocationField, NoPermission, OfflineBanner, OnlineOnlyNotice, WarehouseField } from "@/features/common/components";
import { useCurrentUser, useDateLanguage, useErrorText, useOnline, useScanner } from "@/features/common/hooks";
import { readQuantity } from "@/features/documents/lines";
import { SubmitResult, useSubmitDocument } from "@/features/documents/submit";
import { canOpenTile } from "@/features/home/tiles";
import { formatDisplayDate, todayIso } from "@/lib/date";
import { formatQuantity, quantityToInput } from "@/lib/quantity";
import { withoutRemarkTag } from "@/offline/sender";
import { spacing } from "@/theme/tokens";
import { AppText, Banner, Button, Card, Field, QuantityField, Screen, SectionTitle, TextField } from "@/ui";
import { countSheetSchema } from "@/validation/schemas";

/**
 * Stock count: create a count sheet from the system balance (online), enter the counted
 * quantities, review and submit. The entered quantities can be sent later from the queue.
 */
export default function CountScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const online = useOnline();
  const language = useDateLanguage();
  const errorText = useErrorText();
  const scan = useScanner();
  const { permissions, timeZone } = useCurrentUser();
  const controller = useSubmitDocument();

  const [warehouse, setWarehouse] = useState<Warehouse | null>(null);
  const [location, setLocation] = useState<Location | null>(null);
  const [sheet, setSheet] = useState<StockDocument | null>(null);
  const [counted, setCounted] = useState<Record<string, string>>({});
  const [filter, setFilter] = useState("");
  const [step, setStep] = useState<"edit" | "review">("edit");
  const [showErrors, setShowErrors] = useState(false);
  const [showWarehouseError, setShowWarehouseError] = useState(false);

  const create = useMutation({
    mutationFn: async () => {
      const request = countSheetSchema.parse({ documentDate: todayIso(timeZone), warehouseId: warehouse?.id, locationId: location?.id ?? null });
      return createCountSheet(request);
    },
    onSuccess: (document) => {
      setSheet(document);
      setCounted({});
      setFilter("");
      setShowErrors(false);
    },
  });

  const checked = useMemo(
    () => (sheet?.lines ?? []).map((line) => ({ line, quantity: readQuantity(counted[line.id] ?? "", { required: true, allowZero: true }) })),
    [counted, sheet],
  );
  const missing = checked.filter((row) => row.quantity.value === null).length;
  const different = checked.filter((row) => row.quantity.value !== null && row.quantity.value !== (row.line.systemQuantity ?? 0)).length;
  const shown = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    return needle === "" ? checked : checked.filter(({ line }) => `${line.itemCode} ${line.itemName} ${line.lotNo ?? ""}`.toLowerCase().includes(needle));
  }, [checked, filter]);

  if (!canOpenTile(permissions, "count")) {
    return (
      <Screen>
        <NoPermission />
      </Screen>
    );
  }

  const reset = () => {
    controller.reset();
    setSheet(null);
    setCounted({});
    setStep("edit");
    setShowErrors(false);
  };

  if (controller.outcome) {
    return (
      <Screen testID="count-result">
        <SubmitResult
          outcome={controller.outcome}
          onDone={() => router.dismissTo("/")}
          onNew={reset}
          onEdit={() => {
            controller.reset();
            setStep("edit");
          }}
        />
      </Screen>
    );
  }

  if (!sheet) {
    return (
      <Screen
        testID="count-start"
        footer={
          <Button
            label={create.isPending ? t("count.creating") : t("count.create")}
            loading={create.isPending}
            disabled={!online}
            onPress={() => {
              setShowWarehouseError(true);
              if (warehouse) {
                create.mutate();
              }
            }}
            testID="count-create"
          />
        }
      >
        {!online ? <OnlineOnlyNotice /> : null}
        {create.error ? <Banner tone="danger" title={t("count.createFailed")} message={errorText(create.error)} testID="count-error" /> : null}
        <AppText style={styles.intro}>{t("count.intro")}</AppText>
        <WarehouseField
          label={t("warehouse.label")}
          value={warehouse}
          onChange={(next) => {
            setWarehouse(next);
            setLocation(null);
          }}
          error={showWarehouseError && !warehouse ? t("validation.warehouse_required") : null}
        />
        <LocationField label={t("count.locationOptional")} value={location} onChange={setLocation} warehouseId={warehouse?.id ?? null} />
      </Screen>
    );
  }

  const submit = () => {
    if (missing > 0) {
      return;
    }
    const lines: SaveStockDocumentLine[] = checked.map(({ line, quantity }) => ({
      itemId: line.itemId,
      quantity: quantity.value ?? 0,
      unitId: line.unitId,
      lotId: line.lotId,
      locationId: line.locationId,
    }));
    void controller.submit({
      kind: "count",
      existingDraft: { id: sheet.id, documentNo: sheet.documentNo },
      payload: {
        documentType: "Count",
        documentDate: sheet.documentDate,
        warehouseId: sheet.warehouseId,
        remark: withoutRemarkTag(sheet.remark) || null,
        lines,
      },
      display: {
        title: `${sheet.documentNo} · ${warehouse?.code ?? ""}`.trim(),
        lines: checked.map(({ line }) => ({ itemCode: line.itemCode, itemName: line.itemName, unitCode: line.unitCode, lotNo: line.lotNo ?? undefined })),
      },
    });
  };

  if (step === "review") {
    const changed = checked.filter((row) => row.quantity.value !== (row.line.systemQuantity ?? 0));
    return (
      <Screen
        testID="count-review"
        footer={
          <>
            <Button label={online ? t("submit.confirm") : t("submit.saveToQueue")} onPress={submit} loading={controller.submitting} testID="count-submit" />
            <Button label={t("common.edit")} variant="secondary" onPress={() => setStep("edit")} disabled={controller.submitting} />
          </>
        }
      >
        <OfflineBanner />
        {controller.error ? <Banner tone="danger" title={t("submit.failedTitle")} message={errorText(controller.error)} /> : null}
        <SectionTitle>{t("submit.reviewTitle")}</SectionTitle>
        <Card>
          <Field label={t("document.number")} value={sheet.documentNo} />
          <Field label={t("warehouse.label")} value={warehouse ? `${warehouse.code} · ${warehouse.name}` : "-"} />
          <Field label={t("document.date")} value={formatDisplayDate(sheet.documentDate, language)} />
          <Field label={t("document.lineCount")} value={String(checked.length)} />
          <Field label={t("count.differences")} value={String(different)} strong />
        </Card>
        {changed.length === 0 ? <Banner tone="info" title={t("count.noDifferences")} /> : <SectionTitle>{t("count.differenceList")}</SectionTitle>}
        {changed.map(({ line, quantity }) => (
          <Card key={line.id}>
            <AppText variant="bodyStrong">{line.itemCode}</AppText>
            <AppText muted>{line.itemName}</AppText>
            <Field label={t("lot.label")} value={line.lotNo ?? "-"} />
            <Field label={t("count.system")} value={`${formatQuantity(line.systemQuantity)} ${line.unitCode}`} />
            <Field label={t("count.counted")} value={`${formatQuantity(quantity.value)} ${line.unitCode}`} strong />
            <Field label={t("count.difference")} value={`${formatQuantity((quantity.value ?? 0) - (line.systemQuantity ?? 0))} ${line.unitCode}`} />
          </Card>
        ))}
      </Screen>
    );
  }

  const scanToFilter = async () => {
    const text = await scan("lot");
    if (text !== null) {
      setFilter(text);
    }
  };

  return (
    <Screen
      scroll={false}
      testID="count-edit"
      footer={
        <>
          {showErrors && missing > 0 ? <Banner tone="danger" title={t("count.missing", { count: missing })} /> : null}
          <Button
            label={t("count.review", { counted: checked.length - missing, total: checked.length })}
            onPress={() => {
              setShowErrors(true);
              if (missing === 0) {
                setStep("review");
              }
            }}
            testID="count-to-review"
          />
        </>
      }
    >
      <OfflineBanner />
      <FlatList
        data={shown}
        keyExtractor={(row) => row.line.id}
        keyboardShouldPersistTaps="handled"
        initialNumToRender={8}
        ListHeaderComponent={
          <View>
            <AppText variant="heading">{sheet.documentNo}</AppText>
            <AppText muted>{`${warehouse?.code ?? ""} · ${formatDisplayDate(sheet.documentDate, language)}`}</AppText>
            <TextField
              label={t("count.filter")}
              value={filter}
              onChangeText={setFilter}
              autoCapitalize="characters"
              autoCorrect={false}
              maxLength={100}
              trailing={<Button label={t("scanner.scan")} variant="secondary" onPress={() => void scanToFilter()} />}
            />
          </View>
        }
        ListEmptyComponent={<AppText muted>{t("common.noResults")}</AppText>}
        renderItem={({ item: { line, quantity } }) => (
          <Card testID={`count-line-${line.lineNo}`}>
            <AppText variant="bodyStrong">{`${line.lineNo}. ${line.itemCode}`}</AppText>
            <AppText muted>{line.itemName}</AppText>
            <Field label={t("lot.label")} value={line.lotNo ?? "-"} />
            <Field label={t("count.system")} value={`${formatQuantity(line.systemQuantity)} ${line.unitCode}`} />
            <QuantityField
              label={t("count.counted")}
              unit={line.unitCode}
              value={counted[line.id] ?? ""}
              onChangeText={(text) => setCounted((current) => ({ ...current, [line.id]: text }))}
              errorCode={showErrors || (counted[line.id] ?? "") !== "" ? quantity.error : null}
            />
            <Button
              label={t("count.sameAsSystem")}
              variant="ghost"
              onPress={() => setCounted((current) => ({ ...current, [line.id]: quantityToInput(line.systemQuantity ?? 0) }))}
            />
          </Card>
        )}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  intro: { marginBottom: spacing.lg },
});
