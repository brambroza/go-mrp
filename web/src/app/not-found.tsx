import { getTranslations } from "next-intl/server";
import Link from "next/link";

import { Button } from "@/components/ui/button";

/** Shown for unknown routes. */
export default async function NotFound() {
  const t = await getTranslations("common");
  return (
    <main className="flex min-h-dvh flex-col items-center justify-center gap-3 p-4 text-center">
      <p className="text-4xl font-semibold">404</p>
      <h1 className="font-medium">{t("state.notFound")}</h1>
      <Button asChild>
        <Link href="/">{t("actions.home")}</Link>
      </Button>
    </main>
  );
}
