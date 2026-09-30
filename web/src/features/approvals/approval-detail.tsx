"use client";

import type { Schemas } from "@mrp/api-client";
import { CheckIcon, Undo2Icon, XIcon } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { useState } from "react";

import { ConfirmDialog } from "@/components/feedback/confirm-dialog";
import { ErrorState } from "@/components/feedback/states";
import { StatusBadge } from "@/components/feedback/status-badge";
import { useUser } from "@/components/providers/auth-provider";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { toLocale } from "@/i18n/config";
import { queryKeys } from "@/lib/api/query-keys";
import { formatDateTime } from "@/lib/format/date";
import { formatMoney } from "@/lib/format/number";
import { useApiMutation, useApiQuery } from "@/lib/hooks/use-api";

import { useDocumentTypeLabel } from "./document-types";

type Request = Schemas["ApprovalRequestDto"];

/** Decisions a user can take on an approval request. */
export type ApprovalDecision = "approve" | "reject" | "withdraw";

/** Longest comment the API accepts (`ApprovalDecisionRequest`). */
const COMMENT_MAX_LENGTH = 1000;

/** Loads one approval request with its history. */
export function useApprovalRequest(id: string | null) {
  return useApiQuery({
    queryKey: [...queryKeys.approvals, "detail", id],
    queryFn: (api) => api.GET("/api/v1/approvals/{id}", { params: { path: { id: id ?? "" } } }),
    enabled: Boolean(id),
  });
}

/** Buttons to approve, reject or withdraw a request, each behind a confirmation dialog. */
export function ApprovalActions({ request, onDone }: { request: Request; onDone?: (request: Request) => void }) {
  const t = useTranslations("approvals");
  const user = useUser();
  const [decision, setDecision] = useState<ApprovalDecision | null>(null);

  const decide = useApiMutation({
    mutationFn: (api, input: { decision: ApprovalDecision; comment: string | null }) =>
      api.POST(`/api/v1/approvals/{id}/${input.decision}`, {
        params: { path: { id: request.id } },
        body: { comment: input.comment },
      }),
    invalidate: [queryKeys.approvals, queryKeys.dashboard],
    successMessage: (_, input) => t(`done.${input.decision}`),
    onSuccess: (updated) => onDone?.(updated),
  });

  const pending = request.status === "Pending";
  const canWithdraw = pending && request.requestedBy === user.id;
  if (!pending || (!request.canAct && !canWithdraw)) {
    return null;
  }

  return (
    <>
      {canWithdraw ? (
        <Button type="button" variant="outline" onClick={() => setDecision("withdraw")} data-testid="approval-withdraw">
          <Undo2Icon data-icon="inline-start" />
          {t("actions.withdraw")}
        </Button>
      ) : null}
      {request.canAct ? (
        <>
          <Button type="button" variant="destructive" onClick={() => setDecision("reject")} data-testid="approval-reject">
            <XIcon data-icon="inline-start" />
            {t("actions.reject")}
          </Button>
          <Button type="button" onClick={() => setDecision("approve")} data-testid="approval-approve">
            <CheckIcon data-icon="inline-start" />
            {t("actions.approve")}
          </Button>
        </>
      ) : null}
      <ConfirmDialog
        open={decision !== null}
        onOpenChange={(open) => (open ? undefined : setDecision(null))}
        title={decision ? t(`confirm.${decision}.title`, { documentNo: request.documentNo }) : ""}
        description={decision ? t(`confirm.${decision}.description`) : undefined}
        confirmLabel={decision ? t(`actions.${decision}`) : undefined}
        destructive={decision === "reject"}
        reason={decision === "reject" ? "required" : "optional"}
        reasonLabel={decision === "reject" ? t("fields.reason") : t("fields.comment")}
        reasonMaxLength={COMMENT_MAX_LENGTH}
        onConfirm={(comment) => (decision ? decide.mutateAsync({ decision, comment }) : undefined)}
      />
    </>
  );
}

/** Facts and history of an approval request. */
export function ApprovalDetail({ request }: { request: Request }) {
  const t = useTranslations("approvals");
  const locale = toLocale(useLocale());
  const user = useUser();
  const documentType = useDocumentTypeLabel();
  const facts: [string, React.ReactNode][] = [
    [t("fields.documentNo"), <span key="no" className="font-medium">{request.documentNo}</span>],
    [t("fields.documentType"), documentType(request.documentType)],
    [t("fields.title"), request.title],
    [t("fields.amount"), formatMoney(request.amount, { locale })],
    [t("fields.requestedBy"), request.requestedByName],
    [t("fields.requestedAt"), formatDateTime(request.requestedAt, { locale, timeZone: user.timeZone })],
    [t("fields.status"), <StatusBadge key="status" status={request.status} />],
    [
      t("fields.step"),
      request.totalSteps > 0
        ? t("stepOf", { step: request.currentStepNo, total: request.totalSteps, name: request.currentStepName ?? "" })
        : t("noSteps"),
    ],
  ];

  return (
    <div className="flex flex-col gap-6">
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
        {facts.map(([label, value]) => (
          <div key={label} className="contents">
            <dt className="text-muted-foreground">{label}</dt>
            <dd className="min-w-0 break-words">{value}</dd>
          </div>
        ))}
      </dl>
      <section aria-labelledby="approval-history">
        <h2 id="approval-history" className="mb-2 font-medium">
          {t("history.title")}
        </h2>
        {request.actions.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("history.empty")}</p>
        ) : (
          <ol className="flex flex-col gap-3 border-l pl-4" data-testid="approval-history">
            {request.actions.map((action, index) => (
              <li key={`${action.stepNo}-${action.actedAt}-${index}`} className="text-sm">
                <p className="font-medium">
                  {t(`history.${action.action}`, { name: action.actedByName, step: action.stepNo })}
                </p>
                <p className="text-xs text-muted-foreground">{formatDateTime(action.actedAt, { locale, timeZone: user.timeZone })}</p>
                {action.comment ? <p className="mt-1 rounded-md bg-muted p-2 whitespace-pre-wrap">{action.comment}</p> : null}
              </li>
            ))}
          </ol>
        )}
      </section>
    </div>
  );
}

/** Loads and shows an approval request; used by the side sheet and by the detail page. */
export function ApprovalDetailLoader({ id, actions = true }: { id: string; actions?: boolean }) {
  const query = useApprovalRequest(id);
  if (query.isLoading) {
    return (
      <div className="flex flex-col gap-3" aria-busy="true">
        <Skeleton className="h-5 w-2/3" />
        <Skeleton className="h-5 w-1/2" />
        <Skeleton className="h-5 w-3/4" />
        <Skeleton className="h-24 w-full" />
      </div>
    );
  }
  if (query.error || !query.data) {
    return <ErrorState error={query.error} onRetry={() => void query.refetch()} />;
  }
  return (
    <div className="flex flex-col gap-6">
      <ApprovalDetail request={query.data} />
      {actions ? (
        <div className="flex flex-wrap justify-end gap-2">
          <ApprovalActions request={query.data} />
        </div>
      ) : null}
    </div>
  );
}
