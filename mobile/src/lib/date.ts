/**
 * Date helpers.
 *
 * Devices in Thailand often run with the Buddhist calendar (`th-TH-u-ca-buddhist`), where locale
 * formatting returns the year 2569 instead of 2026. Every date sent to the API is therefore built
 * from explicit Gregorian parts and never from locale formatting.
 */

/** Difference between the Buddhist era and the Gregorian year. */
export const BUDDHIST_ERA_OFFSET = 543;

/** Default time zone of tenants (the API sends the tenant's own zone in the user profile). */
export const DEFAULT_TIME_ZONE = "Asia/Bangkok";

/** Fixed offset used when `Intl` cannot resolve the time zone (Thailand has no DST). */
const FALLBACK_OFFSET_MINUTES = 7 * 60;

/** Languages of the app. */
export type DateLanguage = "th" | "en";

/** Gregorian calendar date. */
export interface DateParts {
  /** Gregorian year, e.g. 2026. */
  year: number;
  /** Month 1-12. */
  month: number;
  /** Day of month 1-31. */
  day: number;
}

/** Date and time of day in a time zone. */
export interface DateTimeParts extends DateParts {
  /** Hour 0-23. */
  hour: number;
  /** Minute 0-59. */
  minute: number;
}

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/** Left-pads a number with zeros. */
function pad(value: number, length: number): string {
  return String(Math.trunc(Math.abs(value))).padStart(length, "0");
}

/** Number of days in a Gregorian month. */
export function daysInMonth(year: number, month: number): number {
  if (month === 2) {
    const leap = (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0;
    return leap ? 29 : 28;
  }
  return [4, 6, 9, 11].includes(month) ? 30 : 31;
}

/** Returns whether the parts form a real Gregorian date between year 1900 and 2200. */
export function isValidDateParts(parts: DateParts): boolean {
  const { year, month, day } = parts;
  if (![year, month, day].every(Number.isInteger)) {
    return false;
  }
  if (year < 1900 || year > 2200 || month < 1 || month > 12 || day < 1) {
    return false;
  }
  return day <= daysInMonth(year, month);
}

/** Builds `YYYY-MM-DD` from Gregorian parts, or `null` when the date does not exist. */
export function isoFromParts(parts: DateParts): string | null {
  if (!isValidDateParts(parts)) {
    return null;
  }
  return `${pad(parts.year, 4)}-${pad(parts.month, 2)}-${pad(parts.day, 2)}`;
}

/** Parses `YYYY-MM-DD` (Gregorian) into parts, or `null` when the text is not a valid date. */
export function parseIsoDate(text: string | null | undefined): DateParts | null {
  const match = ISO_DATE.exec((text ?? "").trim());
  if (!match) {
    return null;
  }
  const parts = { year: Number(match[1]), month: Number(match[2]), day: Number(match[3]) };
  return isValidDateParts(parts) ? parts : null;
}

/** Reads the wall-clock parts of an instant in a time zone using a forced Gregorian calendar. */
function zonedPartsWithIntl(instant: Date, timeZone: string): DateTimeParts | null {
  try {
    const formatter = new Intl.DateTimeFormat("en-US-u-ca-gregory-nu-latn", {
      timeZone,
      calendar: "gregory",
      numberingSystem: "latn",
      hourCycle: "h23",
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
    });
    const found: Record<string, number> = {};
    for (const part of formatter.formatToParts(instant)) {
      if (part.type !== "literal") {
        found[part.type] = Number(part.value);
      }
    }
    const parts: DateTimeParts = {
      year: found.year ?? Number.NaN,
      month: found.month ?? Number.NaN,
      day: found.day ?? Number.NaN,
      hour: (found.hour ?? Number.NaN) % 24,
      minute: found.minute ?? Number.NaN,
    };
    const timeOk = Number.isInteger(parts.hour) && Number.isInteger(parts.minute);
    // A Buddhist year slipping through (engine ignoring the calendar) is rejected here.
    const plausible = Math.abs(parts.year - instant.getUTCFullYear()) <= 1;
    return isValidDateParts(parts) && timeOk && plausible ? parts : null;
  } catch {
    return null;
  }
}

/** Wall-clock parts using a fixed UTC offset; used only when `Intl` is unavailable. */
function zonedPartsWithOffset(instant: Date, offsetMinutes: number): DateTimeParts {
  const shifted = new Date(instant.getTime() + offsetMinutes * 60_000);
  return {
    year: shifted.getUTCFullYear(),
    month: shifted.getUTCMonth() + 1,
    day: shifted.getUTCDate(),
    hour: shifted.getUTCHours(),
    minute: shifted.getUTCMinutes(),
  };
}

/** Wall-clock date and time of an instant in a time zone (Gregorian). */
export function zonedDateTimeParts(instant: Date, timeZone: string = DEFAULT_TIME_ZONE): DateTimeParts {
  return zonedPartsWithIntl(instant, timeZone) ?? zonedPartsWithOffset(instant, FALLBACK_OFFSET_MINUTES);
}

/** ISO Gregorian date (`YYYY-MM-DD`) of an instant in a time zone. Safe under a Thai locale. */
export function toIsoDate(instant: Date, timeZone: string = DEFAULT_TIME_ZONE): string {
  const parts = zonedDateTimeParts(instant, timeZone);
  return `${pad(parts.year, 4)}-${pad(parts.month, 2)}-${pad(parts.day, 2)}`;
}

/** Today's ISO Gregorian date in the tenant's time zone. */
export function todayIso(timeZone: string = DEFAULT_TIME_ZONE, now: Date = new Date()): string {
  return toIsoDate(now, timeZone);
}

/** Converts a Gregorian year to the year shown in the given language (Buddhist era for Thai). */
export function displayYear(gregorianYear: number, language: DateLanguage): number {
  return language === "th" ? gregorianYear + BUDDHIST_ERA_OFFSET : gregorianYear;
}

/** Converts a year typed by the user in the given language back to a Gregorian year. */
export function gregorianYearFromDisplay(year: number, language: DateLanguage): number {
  return language === "th" ? year - BUDDHIST_ERA_OFFSET : year;
}

/**
 * Formats an ISO date for display as `DD/MM/YYYY`; Thai shows the Buddhist-era year.
 * Returns an empty string for a missing or invalid date.
 */
export function formatDisplayDate(iso: string | null | undefined, language: DateLanguage): string {
  const parts = parseIsoDate((iso ?? "").slice(0, 10));
  if (!parts) {
    return "";
  }
  return `${pad(parts.day, 2)}/${pad(parts.month, 2)}/${displayYear(parts.year, language)}`;
}

/**
 * Formats an ISO timestamp (UTC from the API) as `DD/MM/YYYY HH:mm` in the tenant's time zone;
 * Thai shows the Buddhist-era year. Returns an empty string for an invalid timestamp.
 */
export function formatDisplayDateTime(
  isoTimestamp: string | null | undefined,
  language: DateLanguage,
  timeZone: string = DEFAULT_TIME_ZONE,
): string {
  if (!isoTimestamp) {
    return "";
  }
  const instant = new Date(isoTimestamp);
  if (Number.isNaN(instant.getTime())) {
    return "";
  }
  const parts = zonedDateTimeParts(instant, timeZone);
  const date = `${pad(parts.day, 2)}/${pad(parts.month, 2)}/${displayYear(parts.year, language)}`;
  return `${date} ${pad(parts.hour, 2)}:${pad(parts.minute, 2)}`;
}

/**
 * Builds an ISO Gregorian date from the day, month and year typed by the user, where the year is
 * in the era of the given language. Returns `null` when the date does not exist.
 */
export function isoFromDisplayInput(day: string, month: string, year: string, language: DateLanguage): string | null {
  const numbers = [day, month, year].map((text) => (/^\d{1,4}$/.test(text.trim()) ? Number(text.trim()) : Number.NaN));
  const [d, m, y] = numbers;
  if (d === undefined || m === undefined || y === undefined || numbers.some(Number.isNaN)) {
    return null;
  }
  return isoFromParts({ year: gregorianYearFromDisplay(y, language), month: m, day: d });
}

/** Compares two ISO dates; negative when `a` is before `b`. */
export function compareIsoDates(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}
