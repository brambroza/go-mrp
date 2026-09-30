"use client";

import { ComingSoon } from "@/components/layout/coming-soon";
import { Permissions } from "@/lib/auth/permissions";

/** Placeholder: /inventory/lots is built on this foundation in the next task. */
export default function Page() {
  return <ComingSoon label="items.lots" permission={Permissions.inventoryRead} />;
}
