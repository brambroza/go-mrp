"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { CustomerList } from "@/features/masters/customers";
import { Permissions } from "@/lib/auth/permissions";

/** Master data page: customers. */
export default function Page() {
  const t = useTranslations("masters");
  return (
    <RequirePermission permission={Permissions.mastersRead}>
      <PageHeader title={t("customers.title")} description={t("customers.description")} />
      <CustomerList />
    </RequirePermission>
  );
}
