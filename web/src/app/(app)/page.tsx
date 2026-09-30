"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { useUser } from "@/components/providers/auth-provider";
import { Dashboard } from "@/features/dashboard/dashboard";

/** Dashboard. */
export default function DashboardPage() {
  const t = useTranslations("dashboard");
  const user = useUser();
  return (
    <>
      <PageHeader title={t("greeting", { name: user.displayName })} description={t("description")} />
      <Dashboard />
    </>
  );
}
