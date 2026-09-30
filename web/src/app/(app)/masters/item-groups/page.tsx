"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { CodedMasterList } from "@/features/masters/coded-master";
import { queryKeys } from "@/lib/api/query-keys";
import { Permissions } from "@/lib/auth/permissions";

/** Master data page: item groups. */
export default function Page() {
  const t = useTranslations("masters");
  return (
    <RequirePermission permission={Permissions.mastersRead}>
      <PageHeader title={t("itemGroups.title")} description={t("itemGroups.description")} />
      <CodedMasterList
        resource="item-groups"
        queryKey={queryKeys.itemGroups}
        labels={{
          caption: t("itemGroups.title"),
          add: t("itemGroups.add"),
          edit: t("itemGroups.edit"),
          view: t("itemGroups.view"),
          saved: t("itemGroups.saved"),
        }}
      />
    </RequirePermission>
  );
}
