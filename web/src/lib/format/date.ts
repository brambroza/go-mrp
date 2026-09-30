import type { AppLocale } from "@/i18n/config";

/** Time zone used when the tenant has none configured. */
export const DEFAULT_TIME_ZONE = "Asia/Bangkok";

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
const EMPTY = "–";

/** Calendar date without time, always Gregorian. */
export interface DateParts {
  /** Gregorian year, for example 2026 (never the Buddhist-era year). */
  year: number;
  /** Month 1-12. */
  month: number;
  /** Day of month 1-31. */
  day: number;
}

/** Options shared by the display formatters. */
export interface DateFormatOptions {
  /** UI locale: `th` shows the Buddhist-era year, `en` the Gregorian year. */
  locale?: AppLocale;
  /** IANA time zone for timestamps (default Asia/Bangkok). */
  timeZone?: string;
  /** Text for empty values (default an en dash). */
  empty?: string;
}

/** Locale tag with explicit calendar and Latin digits, so the OS locale never leaks in. */
function displayLocale(locale: AppLocale | undefined): string {
  return locale === "en" ? "en-GB-u-ca-gregory-nu-latn" : "th-TH-u-ca-buddhist-nu-latn";
}

function pad(value: number, length: number): string {
  return String(value).padStart(length, "0");
}

/** Builds `YYYY-MM-DD` from Gregorian parts. */
export function partsToIsoDate(parts: DateParts): string {
  return `${pad(parts.year, 4)}-${pad(parts.month, 2)}-${pad(parts.day, 2)}`;
}

/** Parses `YYYY-MM-DD`; returns `null` when the text is not a real calendar date. */
export function parseIsoDate(text: string | null | undefined): DateParts | null {
  const match = ISO_DATE.exec(text ?? "");
  if (!match) {
    return null;
  }
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const probe = new Date(Date.UTC(year, month - 1, day));
  probe.setUTCFullYear(year);
  const real = probe.getUTCFullYear() === year && probe.getUTCMonth() === month - 1 && probe.getUTCDate() === day;
  return real && year >= 1 ? { year, month, day } : null;
}

/** Whether the text is a valid `YYYY-MM-DD` date. */
export function isIsoDate(text: string | null | undefined): text is string {
  return parseIsoDate(text) !== null;
}

/**
 * Gregorian calendar date of an instant in a time zone, as `YYYY-MM-DD` for the API.
 * Uses an explicit `gregory` calendar: a Thai OS/browser locale must never produce year 2569.
 */
export function toIsoDate(instant: Date, timeZone: string = DEFAULT_TIME_ZONE): string {
  const parts = new Intl.DateTimeFormat("en-US-u-ca-gregory-nu-latn", {
    timeZone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(instant);
  const pick = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find((part) => part.type === type)?.value);
  return partsToIsoDate({ year: pick("year"), month: pick("month"), day: pick("day") });
}

/** Today's date in the tenant time zone as `YYYY-MM-DD`. */
export function todayIso(timeZone: string = DEFAULT_TIME_ZONE, now: Date = new Date()): string {
  return toIsoDate(now, timeZone);
}

/** Converts a `Date` picked in a calendar widget (local midnight) to `YYYY-MM-DD`. */
export function localDateToIso(date: Date): string {
  return partsToIsoDate({ year: date.getFullYear(), month: date.getMonth() + 1, day: date.getDate() });
}

/** Converts `YYYY-MM-DD` to a `Date` at local midnight for calendar widgets; `undefined` when invalid. */
export function isoToLocalDate(text: string | null | undefined): Date | undefined {
  const parts = parseIsoDate(text);
  if (!parts) {
    return undefined;
  }
  const date = new Date(parts.year, parts.month - 1, parts.day);
  date.setFullYear(parts.year);
  return date;
}

/**
 * Formats a date for display, for example `29 ก.ย. 2569` (th) or `29 Sep 2026` (en).
 * Accepts a date-only string (shown as is, no time zone shift) or a UTC timestamp
 * (converted to the time zone first).
 */
export function formatDate(value: string | Date | null | undefined, options: DateFormatOptions = {}): string {
  if (value === null || value === undefined || value === "") {
    return options.empty ?? EMPTY;
  }
  const format = (instant: Date, timeZone: string) =>
    new Intl.DateTimeFormat(displayLocale(options.locale), {
      timeZone,
      day: "numeric",
      month: "short",
      year: "numeric",
    }).format(instant);

  if (typeof value === "string") {
    const parts = parseIsoDate(value);
    if (parts) {
      const utc = new Date(Date.UTC(parts.year, parts.month - 1, parts.day));
      utc.setUTCFullYear(parts.year);
      return format(utc, "UTC");
    }
  }
  const instant = typeof value === "string" ? new Date(value) : value;
  return Number.isNaN(instant.getTime()) ? (options.empty ?? EMPTY) : format(instant, options.timeZone ?? DEFAULT_TIME_ZONE);
}

/** Formats a UTC timestamp with time, for example `29 ก.ย. 2569 14:05`. */
export function formatDateTime(value: string | Date | null | undefined, options: DateFormatOptions = {}): string {
  if (value === null || value === undefined || value === "") {
    return options.empty ?? EMPTY;
  }
  const instant = typeof value === "string" ? new Date(value) : value;
  if (Number.isNaN(instant.getTime())) {
    return options.empty ?? EMPTY;
  }
  return new Intl.DateTimeFormat(displayLocale(options.locale), {
    timeZone: options.timeZone ?? DEFAULT_TIME_ZONE,
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).format(instant);
}
