import { useQuery } from "@tanstack/react-query";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import type { SaveStockDocumentLine } from "@/api/inventory";
import type { Location, Warehouse } from "@/api/masters";
import { fetchOrder, type PurchaseOrderLine } from "@/api/purchasing";
import { LocationField, NoPermission, OfflineBanner, QueryError, WarehouseField } from "@/features/common/components";
import { useCurrentUser, useDateLanguage, useErrorText, useOnline } from "@/features/common/hooks";
import { outstandingInOrderUnit, readQuantity, textOrNull } from "@/features/documents/lines";
import { SubmitResult, useSubmitDocument } from "@/features/documents/submit";
import { canOpenTile } from "@/features/home/tiles";
import { formatDisplayDate, todayIso } from "@/lib/date";
import { formatQuantity } from "@/lib/quantity";
import { isUuid } from "@/lib/uuid";
import { AppText, Banner, Button, Card, DateField, Field, LoadingView, QuantityField, Screen, SectionTitle, TextField } from "@/ui";

/** What the user typed for one PO line. */
interface LineInput {
  quantity: string;
  supplierLot: string;
  mfgDate: string | null;
  expiryDate: string | null;
  location: Location | null;
  /** `true` while the manufacturing date field holds text that is not a real date. */
  badMfg: boolean;
  /** `true` while the expiry date field holds text that is not a real date. */
  badExpiry: boolean;
}

const EMPTY_LINE: LineInput = { quantity: "", supplierLot: "", mfgDate: null, expiryDate: null, location: null, badMfg: false, badExpiry: false };

/** Goods receipt, steps 2-4: enter received quantities per PO line, review, submit. */
export default function ReceiveOrderScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const online = useOnline();
  const language = useDateLanguage();
  const errorText = useErrorText();
  const { permissions, timeZone } = useCurrentUser();
  const { poId } = useLocalSearchParams<{ poId: string }>();
  const validId = isUuid(poId);

  const order = useQuery({ queryKey: ["purchase-order", poId], queryFn: () => fetchOrder(poId), enabled: validId, staleTime: 0 });
  const controller = useSubmitDocument();

  const [warehouse, setWarehouse] = useState<Warehouse | null>(null);
  const [inputs, setInputs] = useState<Record<string, LineInput>>({});
  const [step, setStep] = useState<"edit" | "review">("edit");
  const [showErrors, setShowErrors] = useState(false);

  const openLines = useMemo(() => (order.data?.lines ?? []).filter((line) => !line.isClosed && line.outstandingStockQuantity > 0), [order.data]);

  const inputOf = (line: PurchaseOrderLine): LineInput => inputs[line.id] ?? EMPTY_LINE;
  const change = (lineId: string, patch: Partial<LineInput>) => setInputs((current) => ({ ...current, [lineId]: { ...(current[lineId] ?? EMPTY_LINE), ...patch } }));

  const checked = openLines.map((line) => {
    const input = inputOf(line);
    const quantity = readQuantity(input.quantity, { required: false });
    const outstanding = outstandingInOrderUnit(line.outstandingStockQuantity, line.conversionFactor);
    const datesWrong = input.mfgDate !== null && input.expiryDate !== null && input.expiryDate < input.mfgDate;
    return {
      line,
      input,
      quantity,
      outstanding,
      received: quantity.value !== null,
      over: quantity.value !== null && quantity.value > outstanding,
      dateError: input.badMfg || input.badExpiry ? "validation.date_invalid" : datesWrong ? "validation.expiry_before_mfg" : null,
    };
  });
  const received = checked.filter((row) => row.received);
  const hasErrors = checked.some((row) => row.quantity.error !== null || (row.received && row.dateError !== null));
  const ready = warehouse !== null && received.length > 0 && !hasErrors;

  if (!canOpenTile(permissions, "receive")) {
    return (
      <Screen>
        <NoPermission />
      </Screen>
    );
  }
  if (!validId) {
    return (
      <Screen>
        <Banner tone="danger" title={t("common.notFound")} />
      </Screen>
    );
  }
  if (order.isLoading) {
    return <LoadingView label={t("common.loading")} />;
  }
  if (!order.data) {
    return (
      <Screen>
        <OfflineBanner />
        <QueryError error={order.error} onRetry={() => void order.refetch()} />
      </Screen>
    );
  }
  const po = order.data;

  if (controller.outcome) {
    return (
      <Screen testID="receive-result">
        <SubmitResult
          outcome={controller.outcome}
          onDone={() => router.dismissTo("/")}
          onNew={() => router.dismissTo("/receive")}
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
    const lines: SaveStockDocumentLine[] = received.map(({ line, input, quantity }) => ({
      itemId: line.itemId,
      quantity: quantity.value ?? 0,
      unitId: line.unitId,
      poLineId: line.id,
      supplierLot: textOrNull(input.supplierLot),
      mfgDate: input.mfgDate,
      expiryDate: input.expiryDate,
      locationId: input.location?.id ?? null,
    }));
    void controller.submit({
      kind: "receipt",
      payload: {
        documentType: "Receipt",
        documentDate: todayIso(timeZone),
        warehouseId: warehouse.id,
        supplierId: po.supplierId,
        referenceType: "PO",
        referenceId: po.id,
        referenceNo: po.documentNo,
        lines,
      },
      display: {
        title: `${po.documentNo} → ${warehouse.code}`,
        lines: received.map(({ line }) => ({ itemCode: line.itemCode, itemName: line.itemName, unitCode: line.unitCode })),
      },
    });
  };

  if (step === "review") {
    return (
      <Screen
        testID="receive-review"
        footer={
          <>
            <Button label={online ? t("submit.confirm") : t("submit.saveToQueue")} onPress={submit} loading={controller.submitting} testID="receive-submit" />
            <Button label={t("common.edit")} variant="secondary" onPress={() => setStep("edit")} disabled={controller.submitting} />
          </>
        }
      >
        <OfflineBanner />
        {controller.error ? <Banner tone="danger" title={t("submit.failedTitle")} message={errorText(controller.error)} /> : null}
        <SectionTitle>{t("submit.reviewTitle")}</SectionTitle>
        <Card>
          <Field label={t("receive.order")} value={po.documentNo} />
          <Field label={t("receive.supplier")} value={po.supplierName} />
          <Field label={t("warehouse.label")} value={warehouse ? `${warehouse.code} · ${warehouse.name}` : "-"} />
          <Field label={t("document.date")} value={formatDisplayDate(todayIso(timeZone), language)} />
        </Card>
        {received.map(({ line, input, quantity, over }) => (
          <Card key={line.id}>
            <AppText variant="bodyStrong">{`${line.lineNo}. ${line.itemCode}`}</AppText>
            <AppText muted>{line.itemName}</AppText>
            <Field label={t("receive.received")} value={`${formatQuantity(quantity.value)} ${line.unitCode}`} strong />
            {over ? <Banner tone="warning" title={t("receive.overTitle")} message={t("receive.overMessage")} /> : null}
            {input.supplierLot.trim() ? <Field label={t("lot.supplierLot")} value={input.supplierLot.trim()} /> : null}
            {input.mfgDate ? <Field label={t("lot.mfg")} value={formatDisplayDate(input.mfgDate, language)} /> : null}
            {input.expiryDate ? <Field label={t("lot.expiry")} value={formatDisplayDate(input.expiryDate, language)} /> : null}
            <Field label={t("location.label")} value={input.location?.code ?? "-"} />
          </Card>
        ))}
      </Screen>
    );
  }

  return (
    <Screen
      testID="receive-edit"
      footer={
        <Button
          label={t("submit.review", { count: received.length })}
          onPress={() => {
            setShowErrors(true);
            if (ready) {
              setStep("review");
            }
          }}
          testID="receive-to-review"
        />
      }
    >
      <OfflineBanner />
      <Card>
        <AppText variant="title">{po.documentNo}</AppText>
        <AppText>{po.supplierName}</AppText>
      </Card>

      <WarehouseField
        label={t("receive.warehouse")}
        value={warehouse}
        onChange={(next) => {
          setWarehouse(next);
          setInputs((current) => Object.fromEntries(Object.entries(current).map(([id, input]) => [id, { ...input, location: null }])));
        }}
        error={showErrors && !warehouse ? t("validation.warehouse_required") : null}
      />

      {showErrors && received.length === 0 ? <Banner tone="danger" title={t("validation.lines_required")} /> : null}
      {openLines.length === 0 ? <Banner tone="info" title={t("receive.nothingOutstanding")} /> : null}

      {checked.map(({ line, input, quantity, outstanding, over, received: isReceived, dateError }) => (
        <Card key={line.id} testID={`receive-line-${line.lineNo}`}>
          <AppText variant="bodyStrong">{`${line.lineNo}. ${line.itemCode}`}</AppText>
          <AppText muted>{line.itemName}</AppText>
          <Field label={t("receive.outstanding")} value={`${formatQuantity(outstanding)} ${line.unitCode}`} strong />
          <QuantityField label={t("receive.received")} unit={line.unitCode} value={input.quantity} onChangeText={(text) => change(line.id, { quantity: text })} errorCode={quantity.error} hint={t("receive.quantityHint")} />
          {over ? <Banner tone="warning" title={t("receive.overTitle")} message={t("receive.overMessage")} /> : null}
          {isReceived ? (
            <>
              <TextField label={t("lot.supplierLot")} value={input.supplierLot} onChangeText={(text) => change(line.id, { supplierLot: text })} autoCapitalize="characters" autoCorrect={false} maxLength={60} />
              <DateField label={t("lot.mfg")} value={input.mfgDate} onChange={(iso) => change(line.id, { mfgDate: iso })} onValidityChange={(bad) => change(line.id, { badMfg: bad })} />
              <DateField
                label={t("lot.expiry")}
                value={input.expiryDate}
                onChange={(iso) => change(line.id, { expiryDate: iso })}
                onValidityChange={(bad) => change(line.id, { badExpiry: bad })}
                error={dateError === "validation.expiry_before_mfg" ? t(dateError) : null}
              />
              <LocationField label={t("location.label")} value={input.location} onChange={(location) => change(line.id, { location })} warehouseId={warehouse?.id ?? null} />
            </>
          ) : null}
        </Card>
      ))}
    </Screen>
  );
}
