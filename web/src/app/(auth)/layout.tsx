import { FactoryIcon } from "lucide-react";
import { getTranslations } from "next-intl/server";
import type { ReactNode } from "react";

import { LanguageSwitch } from "@/components/shell/language-switch";
import { ThemeToggle } from "@/components/shell/theme-toggle";

/** Layout of the sign-in and sign-up pages. */
export default async function AuthLayout({ children }: { children: ReactNode }) {
  const t = await getTranslations("common");
  return (
    <div className="flex min-h-dvh flex-col bg-muted/40">
      <header className="flex items-center justify-between p-3">
        <div className="flex items-center gap-2 font-semibold">
          <span className="flex size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground">
            <FactoryIcon className="size-4" />
          </span>
          {t("appName")}
        </div>
        <div className="flex items-center gap-1">
          <LanguageSwitch />
          <ThemeToggle />
        </div>
      </header>
      <main className="flex flex-1 items-start justify-center p-4 sm:items-center">{children}</main>
    </div>
  );
}
