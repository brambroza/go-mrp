"use client";

import { ComingSoon } from "@/components/layout/coming-soon";
import { Permissions } from "@/lib/auth/permissions";

/** Placeholder: /purchasing/requests is built on this foundation in the next task. */
export default function Page() {
  return <ComingSoon label="items.purchaseRequests" permission={Permissions.purchasingRead} />;
}
