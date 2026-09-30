import { useMutation, useQuery } from "@tanstack/react-query";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useState } from "react";
import { useTranslation } from "react-i18next";

import { NoPermission, OfflineBanner, QueryError } from "@/features/common/components";
import { useCurrentUser, useDateLanguage, useErrorText, useOnline } from "@/features/common/hooks";
import { QUEUE_TONE } from "@/features/common/status";
import { readQuantity } from "@/features/documents/lines";
import { canOpenTile } from "@/features/home/tiles";
import { formatDisplayDate, formatDisplayDateTime } from "@/lib/date";
import { formatQuantity, quantityToInput } from "@/lib/quantity";
import { isUuid } from "@/lib/uuid";
import { withoutRemarkTag } from "@/offline/sender";
import { discardItem, editItem, getQueueStore, retryItem } from "@/offline/service";
import type { QueueItem } from "@/offline/types";
import { AppText, Badge, Banner, Button, Card, ConfirmDialog, Field, LoadingView, QuantityField, Screen, SectionTitle } from "@/ui";

/** Edited state of one line: quantity text and whether the line is removed. */
interface LineEdit {
  quantity: string;
  removed: boolean;
}

/** Initial edit state from the payload of an item. */
function editsOf(item: QueueItem): LineEdit[] {
  return item.payload.lines.map((line) => ({ quantity: quantityToInput(line.quantity), removed: false }));
}

/**
 * One queue item: status, error of the API, lines. A pending or failed item can be corrected
 * (quantities, removing lines), sent again or discarded.
 */
export default function QueueItemScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const online = useOnline();
  const language = useDateLanguage();
  const errorText = useErrorText();
  const { permissions, scope, timeZone } = useCurrentUser();
  const { id } = useLocalSearchParams<{ id: string }>();
  const validId = isUuid(id);

  const [edits, setEdits] = useState<LineEdit[] | null>(null);
  const [editing, setEditing] = useState(false);
  const [discarding, setDiscarding] = useState(false);

  const item = useQuery({
    queryKey: ["queue", "item", scope?.tenantId, scope?.userId, id],
    queryFn: async () => (scope ? (await getQueueStore()).get(scope, id) : null),
    enabled: scope !== null && validId,
  });

  const send = useMutation({
    mutationFn: async () => {
      if (!scope || !item.data) {
        return null;
      }
      const current = item.data;
      if (editing) {
        const changes = edits ?? editsOf(current);
        const kept = current.payload.lines.flatMap((line, index) => {
          const edit = changes[index];
          const quantity = edit ? readQuantity(edit.quantity, { required: true, allowZero: current.kind === "count" }) : null;
          return edit && !edit.removed && quantity?.value !== null && quantity?.value !== undefined ? [{ line: { ...line, quantity: quantity.value }, label: current.display.lines[index] }] : [];
        });
        await editItem(
          scope,
          current.id,
          { ...current.payload, lines: kept.map((row) => row.line) },
          { ...current.display, lines: kept.map((row) => row.label ?? { itemCode: "", itemName: "" }) },
        );
      }
      return retryItem(scope, current.id, online);
    },
    onSuccess: () => {
      setEditing(false);
      setEdits(null);
      void item.refetch();
    },
  });

  const discard = useMutation({
    mutationFn: async () => {
      if (scope) {
        await discardItem(scope, id);
      }
    },
    onSuccess: () => {
      setDiscarding(false);
      router.back();
    },
  });

  if (!canOpenTile(permissions, "queue")) {
    return (
      <Screen>
        <NoPermission />
      </Screen>
    );
  }
  if (item.isLoading) {
    return <LoadingView label={t("common.loading")} />;
  }
  if (item.error) {
    return (
      <Screen>
        <QueryError error={item.error} onRetry={() => void item.refetch()} />
      </Screen>
    );
  }
  if (!validId || !item.data) {
    return (
      <Screen>
        <Banner tone="warning" title={t("queue.notFound")} />
        <Button label={t("common.back")} variant="secondary" onPress={() => router.back()} />
      </Screen>
    );
  }

  const data = item.data;
  const canChange = data.status === "pending" || data.status === "failed";
  const canEditLines = canChange && data.kind !== "count";
  const waitingApproval = data.status === "sent" && data.serverStatus !== "Posted";
  const checked = (edits ?? editsOf(data)).map((edit) => ({ edit, quantity: readQuantity(edit.quantity, { required: true, allowZero: data.kind === "count" }) }));
  const keptCount = checked.filter((row) => !row.edit.removed).length;
  const editValid = keptCount > 0 && checked.every((row) => row.edit.removed || row.quantity.error === null);
  const change = (index: number, patch: Partial<LineEdit>) => setEdits((current) => (current ?? editsOf(data)).map((edit, position) => (position === index ? { ...edit, ...patch } : edit)));

  return (
    <Screen
      testID="queue-item-screen"
      footer={
        canChange ? (
          <>
            <Button
              label={editing ? t("queue.saveAndSend") : online ? t("queue.sendAgain") : t("queue.sendWhenOnline")}
              onPress={() => send.mutate()}
              loading={send.isPending}
              disabled={(editing && !editValid) || (!editing && !online && data.status === "pending")}
              testID="queue-send"
            />
            {canEditLines && !editing ? <Button label={t("queue.edit")} variant="secondary" onPress={() => setEditing(true)} disabled={send.isPending} testID="queue-edit" /> : null}
            {editing ? (
              <Button
                label={t("queue.cancelEdit")}
                variant="secondary"
                disabled={send.isPending}
                onPress={() => {
                  setEditing(false);
                  setEdits(null);
                }}
              />
            ) : null}
            <Button label={t("queue.discard")} variant="danger" onPress={() => setDiscarding(true)} disabled={send.isPending} testID="queue-discard" />
          </>
        ) : (
          <Button label={t("common.back")} variant="secondary" onPress={() => router.back()} />
        )
      }
    >
      <OfflineBanner />
      {send.error ? <Banner tone="danger" title={t("submit.failedTitle")} message={errorText(send.error)} /> : null}
      {discard.error ? <Banner tone="danger" title={t("queue.discardFailed")} message={errorText(discard.error)} /> : null}

      <Card>
        <AppText variant="title">{t(`queue.kind.${data.kind}`)}</AppText>
        <Badge label={waitingApproval ? t("queue.status.approval") : t(`queue.status.${data.status}`)} tone={waitingApproval ? "warning" : QUEUE_TONE[data.status]} />
        <Field label={t("document.title")} value={data.display.title} />
        {data.serverDocumentNo ? <Field label={t("document.number")} value={data.serverDocumentNo} strong /> : null}
        <Field label={t("document.date")} value={formatDisplayDate(data.payload.documentDate, language)} />
        <Field label={t("queue.savedAt")} value={formatDisplayDateTime(data.createdAt, language, timeZone)} />
        {data.sentAt ? <Field label={t("queue.sentAt")} value={formatDisplayDateTime(data.sentAt, language, timeZone)} /> : null}
        <Field label={t("queue.attempts")} value={String(data.attempts)} />
        {withoutRemarkTag(data.payload.remark) ? <Field label={t("document.remark")} value={withoutRemarkTag(data.payload.remark)} /> : null}
      </Card>

      {data.status === "failed" ? (
        <Banner
          tone="danger"
          title={t("queue.failedTitle")}
          message={`${errorText({ code: data.lastErrorCode, detail: data.lastErrorMessage, status: data.lastErrorStatus })}\n${t("queue.failedHint")}`}
          testID="queue-error"
        />
      ) : null}
      {data.status === "pending" && data.lastErrorCode ? (
        <Banner tone="warning" title={t("queue.waitingTitle")} message={errorText({ code: data.lastErrorCode, detail: data.lastErrorMessage, status: data.lastErrorStatus })} />
      ) : null}
      {waitingApproval ? <Banner tone="warning" title={t("submit.approvalTitle")} message={t("submit.approvalMessage")} /> : null}
      {data.status === "sent" && !waitingApproval ? <Banner tone="success" title={t("submit.postedTitle")} /> : null}
      {canChange && data.serverDocumentId ? <Banner tone="info" title={t("queue.draftExistsTitle")} message={t("queue.draftExistsMessage", { no: data.serverDocumentNo ?? "" })} /> : null}

      <SectionTitle>{t("document.lines")}</SectionTitle>
      {editing && keptCount === 0 ? <Banner tone="danger" title={t("validation.lines_required")} /> : null}
      {data.payload.lines.map((line, index) => {
        const label = data.display.lines[index];
        const row = checked[index];
        const removed = row?.edit.removed ?? false;
        return (
          <Card key={`${line.itemId}-${index}`}>
            <AppText variant="bodyStrong">{`${index + 1}. ${label?.itemCode ?? ""}`}</AppText>
            <AppText muted>{label?.itemName ?? ""}</AppText>
            {label?.lotNo ? <Field label={t("lot.label")} value={label.lotNo} /> : null}
            {line.supplierLot ? <Field label={t("lot.supplierLot")} value={line.supplierLot} /> : null}
            {line.expiryDate ? <Field label={t("lot.expiry")} value={formatDisplayDate(line.expiryDate, language)} /> : null}
            {editing && row && !removed ? (
              <>
                <QuantityField label={t("document.quantity")} unit={label?.unitCode} value={row.edit.quantity} onChangeText={(text) => change(index, { quantity: text })} errorCode={row.quantity.error} />
                <Button label={t("document.removeLine")} variant="ghost" onPress={() => change(index, { removed: true })} />
              </>
            ) : null}
            {editing && removed ? (
              <>
                <Badge label={t("queue.lineRemoved")} tone="danger" />
                <Button label={t("queue.restoreLine")} variant="ghost" onPress={() => change(index, { removed: false })} />
              </>
            ) : null}
            {!editing ? <Field label={t("document.quantity")} value={`${formatQuantity(line.quantity)} ${label?.unitCode ?? ""}`.trim()} strong /> : null}
          </Card>
        );
      })}

      <ConfirmDialog
        visible={discarding}
        title={t("queue.discardTitle")}
        message={data.serverDocumentId ? t("queue.discardWithDraft", { no: data.serverDocumentNo ?? "" }) : t("queue.discardMessage")}
        confirmLabel={t("queue.discard")}
        cancelLabel={t("common.cancel")}
        destructive
        busy={discard.isPending}
        onConfirm={() => discard.mutate()}
        onCancel={() => setDiscarding(false)}
      />
    </Screen>
  );
}
