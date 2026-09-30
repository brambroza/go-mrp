"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";

import { normalizeError, problemMessage } from "@/lib/api/problem";

/**
 * Returns a function that turns any error of an API call into a message for the user:
 * the translation of the problem `code` when `errors.json` has one, otherwise the API's `detail`.
 */
export function useProblemMessage(): (error: unknown) => string {
  const t = useTranslations("errors");
  return useCallback(
    (error: unknown) =>
      problemMessage(
        normalizeError(error),
        (code) => (t.has(code as never) ? t(code as never) : undefined),
        t("generic"),
      ),
    [t],
  );
}
