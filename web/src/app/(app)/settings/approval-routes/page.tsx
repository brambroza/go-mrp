"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { ApprovalRouteList } from "@/features/settings/approval-routes";
import { Permissions } from "@/lib/auth/permissions";

/** Settings page: approval-routes. */
export default function Page() {
  const t = useTranslations("settings");
  return (
    <RequirePermission permission={Permissions.approvalsConfigure}>
      <PageHeader title={t("routes.title")} description={t("routes.description")} />
      <ApprovalRouteList />
    </RequirePermission>
  );
}
