import type { AppLocale } from "@/i18n/config";

/** Decimal places the API stores for money (`numeric(18,4)`). */
export const MONEY_DECIMALS = 4;

/** Decimal places the API stores for quantities (`numeric(18,6)`). */
export const QUANTITY_DECIMALS = 6;

/** Options of {@link formatMoney}. */
export interface MoneyFormatOptions {
  /** UI locale; only affects grouping conventions, digits are always Latin. */
  locale?: AppLocale;
  /** ISO currency code; `THB` is shown as the baht sign, other codes as a suffix. */
  currency?: string | null;
  /** Minimum decimals shown (default 2). */
  minDecimals?: number;
  /** Maximum decimals shown (default 4, the storage precision). */
  maxDecimals?: number;
  /** Text for `null`/`undefined` (default an en dash). */
  empty?: string;
}

/** Options of {@link formatQuantity}. */
export interface QuantityFormatOptions {
  /** UI locale; digits are always Latin. */
  locale?: AppLocale;
  /** Minimum decimals shown (default 0). */
  minDecimals?: number;
  /** Maximum decimals shown (default 6, the storage precision). */
  maxDecimals?: number;
  /** Unit code appended after the number. */
  unit?: string | null;
  /** Text for `null`/`undefined` (default an en dash). */
  empty?: string;
}

const EMPTY = "–";

/** Maps the UI locale to a BCP 47 tag that always renders Latin digits. */
function numberLocale(locale: AppLocale | undefined): string {
  return locale === "en" ? "en-US-u-nu-latn" : "th-TH-u-nu-latn";
}

/**
 * Rounds half away from zero to the given number of decimals, the same rule the API uses.
 * Works on the decimal string representation to avoid binary floating point drift (1.005 → 1.01).
 */
export function roundTo(value: number, decimals: number): number {
  if (!Number.isFinite(value)) {
    return value;
  }
  const sign = value < 0 ? -1 : 1;
  const magnitude = Math.abs(value);
  if (magnitude >= 1e15) {
    // Beyond this size a double has no fractional digits left to round.
    return value;
  }
  const text = String(magnitude);
  if (text.includes("e")) {
    // Tiny magnitudes are printed in exponent form; plain scaling is exact enough for them.
    const factor = 10 ** decimals;
    return (sign * Math.round(magnitude * factor)) / factor || 0;
  }
  const rounded = Math.round(Number(`${text}e${decimals}`));
  return sign * Number(`${rounded}e-${decimals}`) || 0;
}

/** Formats a money amount with thousands separators, for example `฿1,234.50`. */
export function formatMoney(value: number | null | undefined, options: MoneyFormatOptions = {}): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return options.empty ?? EMPTY;
  }
  const minDecimals = options.minDecimals ?? 2;
  const maxDecimals = Math.max(minDecimals, options.maxDecimals ?? MONEY_DECIMALS);
  const text = new Intl.NumberFormat(numberLocale(options.locale), {
    minimumFractionDigits: minDecimals,
    maximumFractionDigits: maxDecimals,
    roundingMode: "halfExpand",
  } as Intl.NumberFormatOptions).format(roundTo(value, maxDecimals));
  const currency = options.currency?.toUpperCase();
  if (!currency) {
    return text;
  }
  return currency === "THB" ? `฿${text}` : `${text} ${currency}`;
}

/** Formats a quantity without trailing zeros, for example `1,250.5 KG`. */
export function formatQuantity(value: number | null | undefined, options: QuantityFormatOptions = {}): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return options.empty ?? EMPTY;
  }
  const minDecimals = options.minDecimals ?? 0;
  const maxDecimals = Math.max(minDecimals, options.maxDecimals ?? QUANTITY_DECIMALS);
  const text = new Intl.NumberFormat(numberLocale(options.locale), {
    minimumFractionDigits: minDecimals,
    maximumFractionDigits: maxDecimals,
  }).format(roundTo(value, maxDecimals));
  return options.unit ? `${text} ${options.unit}` : text;
}

/** Formats a percentage value that is already expressed in percent (7 → `7%`). */
export function formatPercent(value: number | null | undefined, options: { empty?: string } = {}): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return options.empty ?? EMPTY;
  }
  return `${formatQuantity(value, { maxDecimals: 2 })}%`;
}

/**
 * Parses what a user typed into a number input. Accepts thousands separators and surrounding
 * spaces; returns `null` for empty input and `undefined` for text that is not a number.
 * The result is rounded to `decimals`.
 */
export function parseDecimal(text: string, decimals: number): number | null | undefined {
  const cleaned = text.replace(/[,\s฿]/g, "");
  if (cleaned === "") {
    return null;
  }
  if (!/^[+-]?(\d+\.?\d*|\.\d+)$/.test(cleaned)) {
    return undefined;
  }
  const value = Number(cleaned);
  return Number.isFinite(value) ? roundTo(value, decimals) : undefined;
}

/** Text shown inside a number input while editing: no grouping, no trailing zeros. */
export function toInputText(value: number | null | undefined, decimals: number): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return "";
  }
  return new Intl.NumberFormat("en-US-u-nu-latn", {
    useGrouping: false,
    minimumFractionDigits: 0,
    maximumFractionDigits: decimals,
  }).format(roundTo(value, decimals));
}
