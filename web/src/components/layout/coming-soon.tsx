"use client";

import { ConstructionIcon } from "lucide-react";
import { useTranslations } from "next-intl";

import { StateMessage } from "@/components/feedback/states";
import type { PermissionRequirement } from "@/lib/auth/permissions";
import type { NavLabelKey } from "@/lib/navigation";

import { PageHeader } from "./page-header";
import { RequirePermission } from "./require-permission";

/** Props of {@link ComingSoon}. */
export interface ComingSoonProps {
  /** Label of the page in `nav.json`, the same key the sidebar uses. */
  label: NavLabelKey;
  /** Permission needed to see the page. */
  permission: PermissionRequirement;
}

/** Placeholder of a page that is planned but not built yet; replace it with the real screen. */
export function ComingSoon({ label, permission }: ComingSoonProps) {
  const t = useTranslations("placeholder");
  const tn = useTranslations("nav");
  return (
    <RequirePermission permission={permission}>
      <PageHeader title={tn(label)} />
      <div className="rounded-lg border border-dashed">
        <StateMessage icon={<ConstructionIcon />} title={t("title")} description={t("description")} />
      </div>
    </RequirePermission>
  );
}
