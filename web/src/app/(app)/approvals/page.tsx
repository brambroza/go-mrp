"use client";

import { useTranslations } from "next-intl";

import { PageHeader } from "@/components/layout/page-header";
import { ApprovalLists } from "@/features/approvals/approval-lists";

/** Approval inbox and my requests. */
export default function ApprovalsPage() {
  const t = useTranslations("approvals");
  return (
    <>
      <PageHeader title={t("title")} description={t("description")} />
      <ApprovalLists />
    </>
  );
}
