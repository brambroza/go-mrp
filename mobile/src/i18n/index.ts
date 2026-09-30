import i18n, { changeLanguage, exists, use as registerPlugin, type TFunction } from "i18next";
import { initReactI18next } from "react-i18next";

import type { AppError } from "@/lib/errors";
import { getDeviceKeyValueStorage } from "@/storage/keyValue";

import enCommon from "./locales/en/common.json";
import enErrors from "./locales/en/errors.json";
import thCommon from "./locales/th/common.json";
import thErrors from "./locales/th/errors.json";

/** Languages of the app. Thai is the default. */
export type AppLanguage = "th" | "en";

const LANGUAGE_KEY = "app.language";

/** Translations by language and namespace. */
export const resources = {
  th: { common: thCommon, errors: thErrors },
  en: { common: enCommon, errors: enErrors },
} as const;

/** Starts i18next with Thai as the default language. Safe to call more than once. */
export function initI18n(): typeof i18n {
  if (!i18n.isInitialized) {
    void registerPlugin(initReactI18next).init({
      resources,
      lng: "th",
      fallbackLng: "th",
      supportedLngs: ["th", "en"],
      ns: ["common", "errors"],
      defaultNS: "common",
      interpolation: { escapeValue: false },
      returnNull: false,
      initAsync: false,
    });
  }
  return i18n;
}

/** Applies the language stored on the device, if any. */
export async function loadStoredLanguage(): Promise<void> {
  try {
    const stored = await getDeviceKeyValueStorage().get(LANGUAGE_KEY);
    if ((stored === "th" || stored === "en") && stored !== i18n.language) {
      await changeLanguage(stored);
    }
  } catch {
    // Keep the default language.
  }
}

/** Changes the language and remembers it on the device. */
export async function setLanguage(language: AppLanguage): Promise<void> {
  await changeLanguage(language);
  await getDeviceKeyValueStorage()
    .set(LANGUAGE_KEY, language)
    .catch(() => undefined);
}

/** Current language. */
export function currentLanguage(): AppLanguage {
  return i18n.language === "en" ? "en" : "th";
}

/** Error data that can be translated: an {@link AppError} or the error stored with a queue item. */
export interface TranslatableError {
  code?: string | null;
  detail?: string | null;
  kind?: AppError["kind"];
  status?: number | null;
}

/**
 * Translates an error for the user: by `code` when a translation exists, else the `detail` of
 * the API, else a general message.
 */
export function translateError(t: TFunction, error: TranslatableError): string {
  if (error.code?.startsWith("validation.") && exists(error.code)) {
    return t(error.code);
  }
  const translate = (key: string | null | undefined): string | null =>
    key && exists(key, { ns: "errors", keySeparator: false }) ? t(key, { ns: "errors", keySeparator: false }) : null;

  const byCode = translate(error.code);
  if (byCode) {
    return byCode;
  }
  if (error.kind && error.kind !== "http") {
    const byKind = translate(`app.${error.kind}`);
    if (byKind) {
      return byKind;
    }
  }
  if (error.detail && error.detail.trim() !== "") {
    return error.detail;
  }
  const byStatus = translate(error.status ? `app.status.${error.status}` : null);
  if (byStatus) {
    return byStatus;
  }
  return t("app.unknown", { ns: "errors", keySeparator: false });
}
