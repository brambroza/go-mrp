import { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";

import type { SaveStockDocument } from "@/api/inventory";
import { useCurrentUser, useErrorText, useOnline } from "@/features/common/hooks";
import { AppError, toAppError } from "@/lib/errors";
import { submitDocument, type SubmitOutcome } from "@/offline/service";
import type { QueueDisplay, QueueKind } from "@/offline/types";
import { Banner, Button, Card, Field } from "@/ui";

/** Document handed to {@link useSubmitDocument}. */
export interface SubmitRequest {
  kind: QueueKind;
  payload: SaveStockDocument;
  display: QueueDisplay;
  /** Draft that already exists on the server (count sheet). */
  existingDraft?: { id: string; documentNo: string };
}

/** State and actions of a document form's submit step. */
export interface SubmitController {
  /** `true` while the document is being saved or sent. */
  submitting: boolean;
  /** Result of the last submit; `null` before the first one. */
  outcome: SubmitOutcome | null;
  /** Error that prevented saving the document at all (validation, storage). */
  error: AppError | null;
  submit: (request: SubmitRequest) => Promise<void>;
  /** Clears the result to start a new document. */
  reset: () => void;
}

/**
 * Submits a document through the offline queue. A second submit from the same form after a
 * rejection replaces the queue item of the first one instead of creating another document.
 */
export function useSubmitDocument(): SubmitController {
  const { scope } = useCurrentUser();
  const online = useOnline();
  const [submitting, setSubmitting] = useState(false);
  const [outcome, setOutcome] = useState<SubmitOutcome | null>(null);
  const [error, setError] = useState<AppError | null>(null);
  const [queueItemId, setQueueItemId] = useState<string | null>(null);

  const submit = useCallback(
    async (request: SubmitRequest) => {
      if (!scope || submitting) {
        return;
      }
      setSubmitting(true);
      setError(null);
      try {
        const result = await submitDocument({ ...request, scope, online, queueItemId });
        setQueueItemId(result.outcome === "sent" ? null : result.item.id);
        setOutcome(result);
      } catch (failure) {
        setError(failure instanceof AppError ? failure : toAppError(failure));
      } finally {
        setSubmitting(false);
      }
    },
    [online, queueItemId, scope, submitting],
  );

  const reset = useCallback(() => {
    setOutcome(null);
    setError(null);
    setQueueItemId(null);
  }, []);

  return { submitting, outcome, error, submit, reset };
}

/** Props of {@link SubmitResult}. */
export interface SubmitResultProps {
  outcome: SubmitOutcome;
  /** Starts a new document of the same kind. */
  onNew: () => void;
  /** Leaves the form. */
  onDone: () => void;
  /** Goes back to the form to correct a rejected document. */
  onEdit: () => void;
}

/**
 * Result of a submit. The four cases look different on purpose: posted, waiting for approval
 * ("รออนุมัติ" — stock has not moved yet), saved on the device, and rejected.
 */
export function SubmitResult({ outcome, onNew, onDone, onEdit }: SubmitResultProps) {
  const { t } = useTranslation();
  const errorText = useErrorText();
  const { item } = outcome;
  const waitingApproval = outcome.outcome === "sent" && item.serverStatus !== "Posted";

  return (
    <>
      {outcome.outcome === "sent" && !waitingApproval ? <Banner tone="success" title={t("submit.postedTitle")} message={t("submit.postedMessage")} testID="result-posted" /> : null}
      {waitingApproval ? <Banner tone="warning" title={t("submit.approvalTitle")} message={t("submit.approvalMessage")} testID="result-approval" /> : null}
      {outcome.outcome === "queued" ? <Banner tone="info" title={t("submit.queuedTitle")} message={t("submit.queuedMessage")} testID="result-queued" /> : null}
      {outcome.outcome === "failed" ? <Banner tone="danger" title={t("submit.failedTitle")} message={errorText(outcome.error)} testID="result-failed" /> : null}

      <Card>
        <Field label={t("document.title")} value={item.display.title} />
        {item.serverDocumentNo ? <Field label={t("document.number")} value={item.serverDocumentNo} strong /> : null}
        <Field label={t("document.lineCount")} value={String(item.payload.lines.length)} />
        <Field label={t("document.status")} value={t(`queue.resultStatus.${waitingApproval ? "approval" : outcome.outcome}`)} />
      </Card>

      {outcome.outcome === "failed" ? <Button label={t("submit.editAndResend")} onPress={onEdit} /> : <Button label={t("submit.newDocument")} onPress={onNew} />}
      <Button label={t("common.backHome")} variant="secondary" onPress={onDone} style={{ marginTop: 8 }} />
    </>
  );
}
