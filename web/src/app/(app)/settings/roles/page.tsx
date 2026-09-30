"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { RoleList } from "@/features/settings/roles";
import { Permissions } from "@/lib/auth/permissions";

/** Settings page: roles. */
export default function Page() {
  const t = useTranslations("settings");
  return (
    <RequirePermission permission={Permissions.rolesManage}>
      <PageHeader title={t("roles.title")} description={t("roles.description")} />
      <RoleList />
    </RequirePermission>
  );
}
