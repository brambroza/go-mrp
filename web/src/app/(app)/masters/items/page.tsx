"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { ItemList } from "@/features/masters/items";
import { Permissions } from "@/lib/auth/permissions";

/** Master data page: items. */
export default function Page() {
  const t = useTranslations("masters");
  return (
    <RequirePermission permission={Permissions.mastersRead}>
      <PageHeader title={t("items.title")} description={t("items.description")} />
      <ItemList />
    </RequirePermission>
  );
}
