"use client";

import { ArrowRightIcon, type LucideIcon, BadgeCheckIcon, ClipboardListIcon, FileTextIcon, SendIcon, ShoppingCartIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import Link from "next/link";

import { useAuth, useUser } from "@/components/providers/auth-provider";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import type { ApiClient } from "@/lib/api/client";
import type { ApiResult } from "@/lib/api/problem";
import { queryKeys } from "@/lib/api/query-keys";
import { Permissions, type PermissionRequirement } from "@/lib/auth/permissions";
import { formatQuantity } from "@/lib/format/number";
import { useApiQuery } from "@/lib/hooks/use-api";
import { navigation, planIncludes, visibleNavigation } from "@/lib/navigation";

/** Key of a counter card in `dashboard.json` → `cards`. */
type CardKey = "pendingApprovals" | "myRequests" | "draftRequests" | "draftOrders" | "draftStockDocuments";

/** A counter of the dashboard: the `total` of a list request with one row. */
interface Counter {
  key: CardKey;
  href: string;
  icon: LucideIcon;
  permission?: PermissionRequirement;
  count: (api: ApiClient) => Promise<ApiResult<{ total: number }>>;
}

const one = { page: 1, pageSize: 1 } as const;

const counters: Counter[] = [
  {
    key: "pendingApprovals",
    href: "/approvals",
    icon: BadgeCheckIcon,
    count: (api) => api.GET("/api/v1/approvals/inbox", { params: { query: one } }),
  },
  {
    key: "myRequests",
    href: "/approvals",
    icon: SendIcon,
    count: (api) => api.GET("/api/v1/approvals/mine", { params: { query: one } }),
  },
  {
    key: "draftRequests",
    href: "/purchasing/requests",
    icon: ClipboardListIcon,
    permission: Permissions.purchasingRead,
    count: (api) => api.GET("/api/v1/purchasing/requests", { params: { query: { ...one, status: "Draft" } } }),
  },
  {
    key: "draftOrders",
    href: "/purchasing/orders",
    icon: ShoppingCartIcon,
    permission: Permissions.purchasingRead,
    count: (api) => api.GET("/api/v1/purchasing/orders", { params: { query: { ...one, status: "Draft" } } }),
  },
  {
    key: "draftStockDocuments",
    href: "/inventory/documents",
    icon: FileTextIcon,
    permission: Permissions.inventoryRead,
    count: (api) => api.GET("/api/v1/inventory/documents", { params: { query: { ...one, status: "Draft" } } }),
  },
];

/** One counter card; loads its own number so a failing module does not break the others. */
function CounterCard({ counter }: { counter: Counter }) {
  const t = useTranslations("dashboard");
  const query = useApiQuery({
    queryKey: [...queryKeys.dashboard, counter.key],
    queryFn: counter.count,
    staleTime: 30_000,
  });
  return (
    <Link href={counter.href} className="rounded-xl focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none">
      <Card className="h-full transition-colors hover:bg-muted/50" data-testid={`dashboard-card-${counter.key}`}>
        <CardHeader>
          <CardDescription className="flex items-center gap-2">
            <counter.icon className="size-4" />
            {t(`cards.${counter.key}.title`)}
          </CardDescription>
          <CardTitle className="text-3xl tabular-nums">
            {query.isLoading ? <Skeleton className="h-9 w-16" /> : query.error ? "–" : formatQuantity(query.data?.total ?? 0)}
          </CardTitle>
        </CardHeader>
        <CardContent className="text-xs text-muted-foreground">
          {query.error ? t("cards.unavailable") : t(`cards.${counter.key}.hint`)}
        </CardContent>
      </Card>
    </Link>
  );
}

/** Dashboard: what waits for the user, drafts to finish, and shortcuts to the pages they may open. */
export function Dashboard() {
  const t = useTranslations("dashboard");
  const tn = useTranslations("nav");
  const user = useUser();
  const { can } = useAuth();

  const links = visibleNavigation(navigation, can)
    .filter((group) => !group.comingSoon && planIncludes(user.plan, group.minPlan) && group.id !== "overview")
    .flatMap((group) => group.items.map((item) => ({ ...item, group: group.label })));

  return (
    <div className="flex flex-col gap-6">
      <section aria-labelledby="dashboard-counters" className="flex flex-col gap-3">
        <h2 id="dashboard-counters" className="font-medium">
          {t("sections.todo")}
        </h2>
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
          {counters
            .filter((counter) => can(counter.permission))
            .map((counter) => (
              <CounterCard key={counter.key} counter={counter} />
            ))}
        </div>
      </section>
      <section aria-labelledby="dashboard-links" className="flex flex-col gap-3">
        <h2 id="dashboard-links" className="font-medium">
          {t("sections.quickLinks")}
        </h2>
        <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {links.map((link) => (
            <li key={link.href}>
              <Link
                href={link.href}
                className="flex items-center gap-3 rounded-lg border bg-card p-3 text-sm transition-colors hover:bg-muted/50 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
              >
                <link.icon className="size-4 shrink-0 text-muted-foreground" />
                <span className="flex min-w-0 flex-col">
                  <span className="truncate font-medium">{tn(link.label)}</span>
                  <span className="truncate text-xs text-muted-foreground">{tn(link.group)}</span>
                </span>
                <ArrowRightIcon className="ml-auto size-4 shrink-0 text-muted-foreground" />
              </Link>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
