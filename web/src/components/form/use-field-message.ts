"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";

import { decodeMessage } from "@/lib/validation/zod";

/**
 * Returns a function that turns the `message` of a form error into text: messages created with
 * `msg()` are translated with the `validation` namespace, server messages are shown as they are.
 */
export function useFieldMessage(): (message: string | undefined) => string | undefined {
  const t = useTranslations("validation");
  return useCallback(
    (message: string | undefined) => {
      const decoded = decodeMessage(message);
      if (!decoded) {
        return message;
      }
      return t.has(decoded.key) ? t(decoded.key, decoded.values) : t("invalid");
    },
    [t],
  );
}
