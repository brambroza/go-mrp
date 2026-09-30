"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { Profile } from "@/features/account/profile";

/** Profile of the signed-in user. */
export default function ProfilePage() {
  const t = useTranslations("account");
  return (
    <>
      <PageHeader title={t("profile.title")} description={t("profile.description")} />
      <Profile />
    </>
  );
}
