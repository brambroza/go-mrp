"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { SupplierList } from "@/features/masters/suppliers";
import { Permissions } from "@/lib/auth/permissions";

/** Master data page: suppliers. */
export default function Page() {
  const t = useTranslations("masters");
  return (
    <RequirePermission permission={Permissions.mastersRead}>
      <PageHeader title={t("suppliers.title")} description={t("suppliers.description")} />
      <SupplierList />
    </RequirePermission>
  );
}
