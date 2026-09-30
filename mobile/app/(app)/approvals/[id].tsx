import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { View } from "react-native";

import { approveRequest, fetchApproval, rejectRequest, type ApprovalRequest } from "@/api/approvals";
import { OnlineOnlyNotice, QueryError } from "@/features/common/components";
import { useCurrentUser, useDateLanguage, useErrorText, useOnline } from "@/features/common/hooks";
import { formatDisplayDateTime } from "@/lib/date";
import { formatMoney } from "@/lib/quantity";
import { isUuid } from "@/lib/uuid";
import { AppText, Badge, Banner, Button, Card, ConfirmDialog, Field, LoadingView, Screen, SectionTitle, TextField, type Tone } from "@/ui";
import { approveCommentSchema, rejectCommentSchema } from "@/validation/schemas";

/** Decision being confirmed. */
type Decision = "approve" | "reject";

const STATUS_TONE: Record<ApprovalRequest["status"], Tone> = { Pending: "warning", Approved: "success", Rejected: "danger", Withdrawn: "neutral" };

/** Approval detail with history; approve, or reject with a reason. Works online only. */
export default function ApprovalDetailScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const online = useOnline();
  const language = useDateLanguage();
  const errorText = useErrorText();
  const queryClient = useQueryClient();
  const { timeZone } = useCurrentUser();
  const { id } = useLocalSearchParams<{ id: string }>();
  const validId = isUuid(id);

  const [decision, setDecision] = useState<Decision | null>(null);
  const [comment, setComment] = useState("");
  const [commentError, setCommentError] = useState<string | null>(null);
  const [done, setDone] = useState<Decision | null>(null);

  const request = useQuery({ queryKey: ["approvals", "detail", id], queryFn: () => fetchApproval(id), enabled: validId && online });

  const decide = useMutation({
    mutationFn: (input: { decision: Decision; comment: string }) =>
      input.decision === "approve" ? approveRequest(id, input.comment === "" ? null : input.comment) : rejectRequest(id, input.comment),
    onSuccess: (updated, input) => {
      queryClient.setQueryData(["approvals", "detail", id], updated);
      void queryClient.invalidateQueries({ queryKey: ["approvals", "inbox"] });
      setDone(input.decision);
      setDecision(null);
      setComment("");
    },
    onError: () => setDecision(null),
  });

  const confirm = () => {
    if (!decision || decide.isPending) {
      return;
    }
    const parsed = (decision === "reject" ? rejectCommentSchema : approveCommentSchema).safeParse(comment);
    if (!parsed.success) {
      setCommentError(t(parsed.error.issues[0]?.message ?? "validation.invalid"));
      return;
    }
    setCommentError(null);
    decide.mutate({ decision, comment: parsed.data });
  };

  if (!validId) {
    return (
      <Screen>
        <Banner tone="danger" title={t("common.notFound")} />
      </Screen>
    );
  }
  if (!online && !request.data) {
    return (
      <Screen>
        <OnlineOnlyNotice />
      </Screen>
    );
  }
  if (request.isLoading) {
    return <LoadingView label={t("common.loading")} />;
  }
  if (!request.data) {
    return (
      <Screen>
        <QueryError error={request.error} onRetry={() => void request.refetch()} />
      </Screen>
    );
  }

  const item = request.data;
  const canAct = item.canAct && item.status === "Pending";

  return (
    <Screen
      testID="approval-detail"
      footer={
        canAct ? (
          <>
            <Button label={t("approvals.approve")} onPress={() => setDecision("approve")} disabled={!online || decide.isPending} testID="approve-button" />
            <Button label={t("approvals.reject")} variant="danger" onPress={() => setDecision("reject")} disabled={!online || decide.isPending} testID="reject-button" />
          </>
        ) : (
          <Button label={t("approvals.backToInbox")} variant="secondary" onPress={() => router.back()} />
        )
      }
    >
      {!online ? <OnlineOnlyNotice /> : null}
      {done === "approve" ? <Banner tone="success" title={t("approvals.approvedTitle")} message={item.status === "Pending" ? t("approvals.nextStep") : t("approvals.approvedMessage")} /> : null}
      {done === "reject" ? <Banner tone="success" title={t("approvals.rejectedTitle")} /> : null}
      {decide.error ? <Banner tone="danger" title={t("approvals.failedTitle")} message={errorText(decide.error)} testID="approval-error" /> : null}

      <Card>
        <AppText variant="title">{item.documentNo}</AppText>
        <Badge label={t(`approvals.status.${item.status}`)} tone={STATUS_TONE[item.status]} />
        <AppText style={{ marginTop: 8 }}>{item.title}</AppText>
        <Field label={t("approvals.documentType")} value={t(`documentType.${item.documentType}`, { defaultValue: item.documentType })} />
        {item.amount !== null ? <Field label={t("approvals.amount")} value={t("common.baht", { amount: formatMoney(item.amount) })} strong /> : null}
        <Field label={t("approvals.requester")} value={item.requestedByName} />
        <Field label={t("approvals.requestedAt")} value={formatDisplayDateTime(item.requestedAt, language, timeZone)} />
        <Field label={t("approvals.currentStep")} value={t("approvals.step", { step: item.currentStepNo, total: item.totalSteps, name: item.currentStepName ?? "" })} />
      </Card>

      {!canAct && item.status === "Pending" ? <Banner tone="info" title={t("approvals.cannotActTitle")} message={t("approvals.cannotActMessage")} /> : null}

      <SectionTitle>{t("approvals.history")}</SectionTitle>
      {item.actions.length === 0 ? <AppText muted>{t("approvals.noHistory")}</AppText> : null}
      {item.actions.map((action, index) => (
        <Card key={`${action.stepNo}-${action.actedAt}-${index}`}>
          <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center", gap: 8 }}>
            <AppText variant="bodyStrong">{t("approvals.stepNo", { step: action.stepNo })}</AppText>
            <Badge label={t(`approvals.action.${action.action}`)} tone={action.action === "Approve" ? "success" : action.action === "Reject" ? "danger" : "neutral"} />
          </View>
          <AppText>{action.actedByName}</AppText>
          <AppText muted>{formatDisplayDateTime(action.actedAt, language, timeZone)}</AppText>
          {action.comment ? <AppText>{t("approvals.comment", { comment: action.comment })}</AppText> : null}
        </Card>
      ))}

      <ConfirmDialog
        visible={decision !== null}
        title={decision === "reject" ? t("approvals.confirmReject", { no: item.documentNo }) : t("approvals.confirmApprove", { no: item.documentNo })}
        message={decision === "reject" ? t("approvals.rejectHint") : undefined}
        confirmLabel={decision === "reject" ? t("approvals.reject") : t("approvals.approve")}
        cancelLabel={t("common.cancel")}
        destructive={decision === "reject"}
        busy={decide.isPending}
        onConfirm={confirm}
        onCancel={() => {
          setDecision(null);
          setCommentError(null);
        }}
      >
        <TextField
          label={decision === "reject" ? t("approvals.reasonRequired") : t("approvals.commentOptional")}
          value={comment}
          onChangeText={(value) => {
            setComment(value);
            setCommentError(null);
          }}
          error={commentError}
          multiline
          maxLength={1000}
          testID="approval-comment"
        />
      </ConfirmDialog>
    </Screen>
  );
}
