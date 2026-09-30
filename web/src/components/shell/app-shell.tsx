"use client";

import { Loader2Icon } from "lucide-react";
import { useTranslations } from "next-intl";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";

import { StateMessage } from "@/components/feedback/states";
import { useAuth } from "@/components/providers/auth-provider";
import { Button } from "@/components/ui/button";
import { SidebarInset, SidebarProvider } from "@/components/ui/sidebar";

import { AppSidebar } from "./app-sidebar";
import { TopBar } from "./top-bar";

/**
 * Frame of every signed-in page. Restores the session from the refresh cookie on the first load,
 * sends the visitor to `/login` when there is none, and renders sidebar and top bar around the page.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const t = useTranslations("common");
  const { status, restore } = useAuth();
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    if (status === "loading") {
      void restore();
    }
  }, [status, restore]);

  useEffect(() => {
    if (status === "unauthenticated") {
      const next = pathname && pathname !== "/" ? `?next=${encodeURIComponent(pathname)}` : "";
      router.replace(`/login${next}`);
    }
  }, [status, pathname, router]);

  if (status === "unavailable") {
    return (
      <main className="flex min-h-dvh items-center justify-center">
        <StateMessage
          title={t("state.unavailableTitle")}
          description={t("state.unavailableDescription")}
          action={
            <Button type="button" onClick={() => void restore()}>
              {t("actions.retry")}
            </Button>
          }
        />
      </main>
    );
  }

  if (status !== "authenticated") {
    return (
      <main className="flex min-h-dvh items-center justify-center gap-2 text-muted-foreground" aria-busy="true">
        <Loader2Icon className="size-5 animate-spin" />
        <span>{t("table.loading")}</span>
      </main>
    );
  }

  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset className="min-w-0">
        <TopBar />
        {/* `SidebarInset` is the <main> landmark of the page. */}
        <div id="content" className="flex min-w-0 flex-1 flex-col gap-4 p-4 md:p-6">
          {children}
        </div>
      </SidebarInset>
    </SidebarProvider>
  );
}
