import type { ReactNode } from "react";

import { AppShell } from "@/components/shell/app-shell";

/** Layout of the pages that need a signed-in user. */
export default function AppLayout({ children }: { children: ReactNode }) {
  return <AppShell>{children}</AppShell>;
}
