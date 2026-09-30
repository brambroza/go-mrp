"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { NumberingList } from "@/features/settings/numbering";
import { Permissions } from "@/lib/auth/permissions";

/** Settings page: numbering. */
export default function Page() {
  const t = useTranslations("settings");
  return (
    <RequirePermission permission={Permissions.settingsManage}>
      <PageHeader title={t("numbering.title")} description={t("numbering.description")} />
      <NumberingList />
    </RequirePermission>
  );
}
