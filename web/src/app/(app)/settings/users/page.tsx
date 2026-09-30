"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { UserList } from "@/features/settings/users";
import { Permissions } from "@/lib/auth/permissions";

/** Settings page: users. */
export default function Page() {
  const t = useTranslations("settings");
  return (
    <RequirePermission permission={Permissions.usersManage}>
      <PageHeader title={t("users.title")} description={t("users.description")} />
      <UserList />
    </RequirePermission>
  );
}
