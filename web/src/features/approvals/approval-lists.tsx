"use client";

import type { Schemas } from "@mrp/api-client";
import { useLocale, useTranslations } from "next-intl";
import { useMemo, useState } from "react";

import { createDataTableColumns, DataTable } from "@/components/data-table/data-table";
import { useListState } from "@/components/data-table/use-list-state";
import { StatusBadge } from "@/components/feedback/status-badge";
import { DetailSheet } from "@/components/layout/detail-sheet";
import { useUser } from "@/components/providers/auth-provider";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { toLocale } from "@/i18n/config";
import { queryKeys } from "@/lib/api/query-keys";
import { formatDateTime } from "@/lib/format/date";
import { formatMoney } from "@/lib/format/number";
import { useApiQuery } from "@/lib/hooks/use-api";

import { ApprovalActions, ApprovalDetail, useApprovalRequest } from "./approval-detail";
import { useDocumentTypeLabel } from "./document-types";

type Request = Schemas["ApprovalRequestDto"];

/** Which list of approval requests is shown. */
export type ApprovalBox = "inbox" | "mine";

/** Table of approval requests: the inbox (waiting for me) or my own requests. */
export function ApprovalTable({ box, onOpen }: { box: ApprovalBox; onOpen: (request: Request) => void }) {
  const t = useTranslations("approvals");
  const locale = toLocale(useLocale());
  const user = useUser();
  const documentType = useDocumentTypeLabel();
  const state = useListState();
  const { page, pageSize } = state.params;

  const query = useApiQuery({
    queryKey: [...queryKeys.approvals, box, { page, pageSize }],
    queryFn: (api) => api.GET(`/api/v1/approvals/${box}`, { params: { query: { page, pageSize } } }),
    keepPrevious: true,
  });

  const columns = useMemo(() => {
    const helper = createDataTableColumns<Request>();
    return [
      helper.accessor((row) => row.documentNo, {
        id: "documentNo",
        header: t("fields.documentNo"),
        meta: { className: "font-medium whitespace-nowrap" },
      }),
      helper.accessor((row) => row.documentType, {
        id: "documentType",
        header: t("fields.documentType"),
        meta: { hideOnMobile: true },
        cell: ({ row }) => documentType(row.original.documentType),
      }),
      helper.accessor((row) => row.title, { id: "title", header: t("fields.title") }),
      helper.accessor((row) => row.amount, {
        id: "amount",
        header: t("fields.amount"),
        meta: { align: "right", className: "tabular-nums whitespace-nowrap" },
        cell: ({ row }) => formatMoney(row.original.amount, { locale }),
      }),
      helper.accessor((row) => row.requestedByName, { id: "requestedBy", header: t("fields.requestedBy"), meta: { hideOnMobile: true } }),
      helper.accessor((row) => row.requestedAt, {
        id: "requestedAt",
        header: t("fields.requestedAt"),
        meta: { hideOnMobile: true, className: "whitespace-nowrap" },
        cell: ({ row }) => formatDateTime(row.original.requestedAt, { locale, timeZone: user.timeZone }),
      }),
      helper.display({
        id: "status",
        header: t("fields.status"),
        cell: ({ row }) => (
          <span className="flex items-center gap-2">
            <StatusBadge status={row.original.status} />
            {row.original.status === "Pending" && row.original.totalSteps > 0 ? (
              <span className="text-xs whitespace-nowrap text-muted-foreground">
                {row.original.currentStepNo}/{row.original.totalSteps}
              </span>
            ) : null}
          </span>
        ),
      }),
    ];
  }, [t, locale, user.timeZone, documentType]);

  return (
    <DataTable
      caption={t(`tabs.${box}`)}
      columns={columns}
      data={query.data}
      loading={query.isLoading}
      fetching={query.isFetching}
      error={query.error}
      onRetry={() => void query.refetch()}
      state={state}
      onRowClick={onOpen}
      emptyTitle={t(`empty.${box}`)}
    />
  );
}

/** Approval inbox and "my requests" with the detail of a request in a side sheet. */
export function ApprovalLists() {
  const t = useTranslations("approvals");
  const [openId, setOpenId] = useState<string | null>(null);
  const detail = useApprovalRequest(openId);

  return (
    <>
      <Tabs defaultValue="inbox">
        <TabsList>
          <TabsTrigger value="inbox">{t("tabs.inbox")}</TabsTrigger>
          <TabsTrigger value="mine">{t("tabs.mine")}</TabsTrigger>
        </TabsList>
        <TabsContent value="inbox" className="mt-3">
          <ApprovalTable box="inbox" onOpen={(request) => setOpenId(request.id)} />
        </TabsContent>
        <TabsContent value="mine" className="mt-3">
          <ApprovalTable box="mine" onOpen={(request) => setOpenId(request.id)} />
        </TabsContent>
      </Tabs>
      <DetailSheet
        open={openId !== null}
        onOpenChange={(open) => (open ? undefined : setOpenId(null))}
        title={detail.data?.documentNo ?? t("detail.title")}
        description={detail.data?.title}
        footer={detail.data ? <ApprovalActions request={detail.data} /> : null}
      >
        {openId ? <ApprovalSheetBody id={openId} /> : null}
      </DetailSheet>
    </>
  );
}

/** Body of the detail sheet: shares the cached request with the sheet's footer. */
function ApprovalSheetBody({ id }: { id: string }) {
  const query = useApprovalRequest(id);
  const t = useTranslations("common");
  if (query.data) {
    return <ApprovalDetail request={query.data} />;
  }
  return <p className="text-sm text-muted-foreground">{query.error ? t("state.errorTitle") : t("table.loading")}</p>;
}
