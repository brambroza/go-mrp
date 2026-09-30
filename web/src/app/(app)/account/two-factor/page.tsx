"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { TwoFactorSetup } from "@/features/account/two-factor";

/** Two-factor authentication setup of the signed-in user. */
export default function TwoFactorPage() {
  const t = useTranslations("account");
  return (
    <>
      <PageHeader title={t("twoFactor.title")} description={t("twoFactor.description")} />
      <TwoFactorSetup />
    </>
  );
}
