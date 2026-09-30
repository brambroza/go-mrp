"use client";

import { ArrowLeftIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import Link from "next/link";
import { use } from "react";

import { StateMessage } from "@/components/feedback/states";
import { PageHeader } from "@/components/layout/page-header";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { ApprovalDetailLoader } from "@/features/approvals/approval-detail";

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Detail of one approval request; the target of links in notifications. */
export default function ApprovalPage({ params }: { params: Promise<{ id: string }> }) {
  const t = useTranslations("approvals");
  const tc = useTranslations("common");
  const { id } = use(params);
  return (
    <>
      <PageHeader
        title={t("detail.title")}
        actions={
          <Button variant="outline" asChild>
            <Link href="/approvals">
              <ArrowLeftIcon data-icon="inline-start" />
              {tc("actions.back")}
            </Link>
          </Button>
        }
      />
      <Card className="max-w-2xl">
        <CardContent>{UUID.test(id) ? <ApprovalDetailLoader id={id} /> : <StateMessage title={tc("state.notFound")} />}</CardContent>
      </Card>
    </>
  );
}
