/** Locales the UI is translated to; the first one is the default. */
export const locales = ["th", "en"] as const;

/** A supported UI locale. */
export type AppLocale = (typeof locales)[number];

/** Locale used when the visitor has not chosen one. */
export const defaultLocale: AppLocale = "th";

/** Cookie that stores the visitor's language choice. */
export const LOCALE_COOKIE = "mrp_locale";

/** Narrows any string to a supported locale, falling back to the default. */
export function toLocale(value: string | null | undefined): AppLocale {
  return locales.find((locale) => locale === value) ?? defaultLocale;
}
