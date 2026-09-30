"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { GeneralSettings } from "@/features/settings/general";
import { Permissions } from "@/lib/auth/permissions";

/** Settings page: general. */
export default function Page() {
  const t = useTranslations("settings");
  return (
    <RequirePermission permission={Permissions.settingsManage}>
      <PageHeader title={t("general.title")} description={t("general.description")} />
      <GeneralSettings />
    </RequirePermission>
  );
}
