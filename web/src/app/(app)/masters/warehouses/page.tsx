"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { WarehouseList } from "@/features/masters/warehouses";
import { Permissions } from "@/lib/auth/permissions";

/** Master data page: warehouses. */
export default function Page() {
  const t = useTranslations("masters");
  return (
    <RequirePermission permission={Permissions.mastersRead}>
      <PageHeader title={t("warehouses.title")} description={t("warehouses.description")} />
      <WarehouseList />
    </RequirePermission>
  );
}
