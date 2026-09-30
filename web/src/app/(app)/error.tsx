"use client";

import { useTranslations } from "next-intl";

import { StateMessage } from "@/components/feedback/states";
import { Button } from "@/components/ui/button";

/** Error boundary of the signed-in pages: keeps the shell and offers to try again. */
export default function AppError({ reset }: { error: Error & { digest?: string }; reset: () => void }) {
  const t = useTranslations("common");
  return (
    <StateMessage
      title={t("state.errorTitle")}
      description={t("state.unexpected")}
      action={
        <Button type="button" onClick={reset}>
          {t("actions.retry")}
        </Button>
      }
    />
  );
}
