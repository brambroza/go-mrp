"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { RequirePermission } from "@/components/layout/require-permission";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { CodedMasterList } from "@/features/masters/coded-master";
import { UnitConversionList } from "@/features/masters/unit-conversions";
import { queryKeys } from "@/lib/api/query-keys";
import { Permissions } from "@/lib/auth/permissions";

/** Master data page: units and unit conversions. */
export default function Page() {
  const t = useTranslations("masters");
  return (
    <RequirePermission permission={Permissions.mastersRead}>
      <PageHeader title={t("units.title")} description={t("units.description")} />
      <Tabs defaultValue="units">
        <TabsList>
          <TabsTrigger value="units">{t("units.tab")}</TabsTrigger>
          <TabsTrigger value="conversions">{t("conversions.title")}</TabsTrigger>
        </TabsList>
        <TabsContent value="units" className="mt-3">
          <CodedMasterList
            resource="units"
            queryKey={queryKeys.units}
            labels={{
              caption: t("units.title"),
              add: t("units.add"),
              edit: t("units.edit"),
              view: t("units.view"),
              saved: t("units.saved"),
            }}
          />
        </TabsContent>
        <TabsContent value="conversions" className="mt-3">
          <UnitConversionList />
        </TabsContent>
      </Tabs>
    </RequirePermission>
  );
}
