"use client";

import { BellIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import Link from "next/link";

import { useUser } from "@/components/providers/auth-provider";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { SidebarTrigger } from "@/components/ui/sidebar";
import { usePendingApprovalCount } from "@/lib/hooks/use-pending-approvals";

import { LanguageSwitch } from "./language-switch";
import { ThemeToggle } from "./theme-toggle";
import { UserMenu } from "./user-menu";

/** Top bar: menu button, company name, package, pending approvals, language, theme and user menu. */
export function TopBar() {
  const t = useTranslations("nav");
  const user = useUser();
  const pending = usePendingApprovalCount() ?? 0;

  return (
    <header className="sticky top-0 z-20 flex h-14 shrink-0 items-center gap-2 border-b bg-background/95 px-3 backdrop-blur">
      <SidebarTrigger aria-label={t("toggleSidebar")} />
      <Separator orientation="vertical" className="mr-1 h-5!" />
      <div className="flex min-w-0 items-center gap-2">
        <span className="truncate font-medium" data-testid="tenant-name">
          {user.tenantName}
        </span>
        <Badge variant="secondary" data-testid="plan-badge">
          {user.plan}
        </Badge>
      </div>
      <div className="ml-auto flex items-center gap-1">
        <Button variant="ghost" size="icon" asChild className="relative">
          <Link href="/approvals" aria-label={t("pendingApprovals", { count: pending })} data-testid="pending-approvals">
            <BellIcon />
            {pending > 0 ? (
              <span className="absolute -top-0.5 -right-0.5 flex min-w-4 items-center justify-center rounded-full bg-destructive px-1 text-[0.65rem] leading-4 font-medium text-white">
                {pending > 99 ? "99+" : pending}
              </span>
            ) : null}
          </Link>
        </Button>
        <LanguageSwitch />
        <ThemeToggle />
        <UserMenu />
      </div>
    </header>
  );
}
