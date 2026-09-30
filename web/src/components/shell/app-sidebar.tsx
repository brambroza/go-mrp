"use client";

import { FactoryIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import Link from "next/link";
import { usePathname } from "next/navigation";

import { useAuth } from "@/components/providers/auth-provider";
import { Badge } from "@/components/ui/badge";
import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuBadge,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarRail,
  useSidebar,
} from "@/components/ui/sidebar";
import { usePendingApprovalCount } from "@/lib/hooks/use-pending-approvals";
import { isActivePath, navigation, planIncludes, visibleNavigation, type NavBadge } from "@/lib/navigation";

/** Sidebar with one group per module; entries the user has no permission for are hidden. */
export function AppSidebar() {
  const t = useTranslations("nav");
  const tc = useTranslations("common");
  const pathname = usePathname();
  const { can, user } = useAuth();
  const { setOpenMobile } = useSidebar();
  const pendingApprovals = usePendingApprovalCount();
  const badges: Record<NavBadge, number | undefined> = { pendingApprovals };

  return (
    <Sidebar collapsible="icon">
      <SidebarHeader>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton size="lg" asChild tooltip={tc("appName")}>
              <Link href="/" onClick={() => setOpenMobile(false)}>
                <span className="flex aspect-square size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground">
                  <FactoryIcon className="size-4" />
                </span>
                <span className="truncate font-semibold">{tc("appName")}</span>
              </Link>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>
      <SidebarContent>
        {visibleNavigation(navigation, can).map((group) => {
          const locked = group.comingSoon || !planIncludes(user?.plan, group.minPlan);
          return (
            <SidebarGroup key={group.id} data-nav-group={group.id}>
              <SidebarGroupLabel>
                {t(group.label)}
                {locked ? (
                  <Badge variant="outline" className="ml-auto text-[0.65rem]">
                    {group.comingSoon ? t("comingSoon") : t("requiresPlan", { plan: group.minPlan ?? "" })}
                  </Badge>
                ) : null}
              </SidebarGroupLabel>
              <SidebarGroupContent>
                <SidebarMenu>
                  {group.items.map((item) => {
                    const count = item.badge ? badges[item.badge] : undefined;
                    const label = t(item.label);
                    return (
                      <SidebarMenuItem key={item.href}>
                        {locked ? (
                          <SidebarMenuButton disabled aria-disabled="true" tooltip={label}>
                            <item.icon />
                            <span>{label}</span>
                          </SidebarMenuButton>
                        ) : (
                          <SidebarMenuButton asChild isActive={isActivePath(pathname, item.href)} tooltip={label}>
                            <Link href={item.href} onClick={() => setOpenMobile(false)}>
                              <item.icon />
                              <span>{label}</span>
                            </Link>
                          </SidebarMenuButton>
                        )}
                        {count ? <SidebarMenuBadge data-testid={`nav-badge-${item.badge}`}>{count > 99 ? "99+" : count}</SidebarMenuBadge> : null}
                      </SidebarMenuItem>
                    );
                  })}
                </SidebarMenu>
              </SidebarGroupContent>
            </SidebarGroup>
          );
        })}
      </SidebarContent>
      <SidebarRail />
    </Sidebar>
  );
}
