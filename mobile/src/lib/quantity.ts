/**
 * Quantity parsing and formatting. The API stores quantities as `numeric(18,6)`, so the app
 * accepts at most 6 decimals and never relies on locale number formatting (Thai numerals,
 * decimal commas).
 */

/** Maximum number of decimals of a quantity. */
export const QUANTITY_DECIMALS = 6;

/** Largest quantity accepted by the API (`Range(…, 999999999999.0)`). */
export const MAX_QUANTITY = 999_999_999_999;

/** Why a quantity text was rejected. */
export type QuantityError = "empty" | "invalid" | "too_many_decimals" | "out_of_range" | "not_positive" | "precision";

/** Result of {@link parseQuantity}. */
export type QuantityParseResult =
  | { ok: true; value: number; normalized: string }
  | { ok: false; error: QuantityError };

/** Options of {@link parseQuantity}. */
export interface ParseQuantityOptions {
  /** Accept zero (stock count). Default `false`. */
  allowZero?: boolean;
  /** Accept negative numbers (adjustments). Default `false`. */
  allowNegative?: boolean;
}

const THAI_ZERO = "๐".charCodeAt(0);

/** Replaces Thai digits (๐-๙) with ASCII digits. */
export function normalizeDigits(text: string): string {
  return text.replace(/[๐-๙]/g, (digit) => String(digit.charCodeAt(0) - THAI_ZERO));
}

/** Removes trailing zeros of the fraction and a dangling decimal point. */
function trimFraction(text: string): string {
  if (!text.includes(".")) {
    return text;
  }
  return text.replace(/0+$/, "").replace(/\.$/, "");
}

/**
 * Parses a quantity typed by the user. Accepts thousands separators (`,`), Thai digits and at
 * most 6 decimals. The value must survive a round trip through a JavaScript number, because the
 * API receives quantities as JSON numbers.
 */
export function parseQuantity(text: string | null | undefined, options: ParseQuantityOptions = {}): QuantityParseResult {
  const cleaned = normalizeDigits(text ?? "")
    .replace(/[\s,]/g, "")
    .trim();
  if (cleaned === "") {
    return { ok: false, error: "empty" };
  }
  const match = /^([+-]?)(\d*)(?:\.(\d*))?$/.exec(cleaned);
  if (!match || ((match[2] ?? "") === "" && (match[3] ?? "") === "")) {
    return { ok: false, error: "invalid" };
  }
  const sign = match[1] === "-" ? "-" : "";
  const integer = (match[2] ?? "").replace(/^0+(?=\d)/, "") || "0";
  const fraction = (match[3] ?? "").replace(/0+$/, "");
  if (fraction.length > QUANTITY_DECIMALS) {
    return { ok: false, error: "too_many_decimals" };
  }
  const isZero = /^0*$/.test(integer) && fraction === "";
  const normalized = `${isZero ? "" : sign}${integer}${fraction ? `.${fraction}` : ""}`;
  const value = Number(normalized);
  if (!Number.isFinite(value)) {
    return { ok: false, error: "invalid" };
  }
  if (Math.abs(value) > MAX_QUANTITY) {
    return { ok: false, error: "out_of_range" };
  }
  if (value < 0 && !options.allowNegative) {
    return { ok: false, error: "not_positive" };
  }
  if (value === 0 && !options.allowZero) {
    return { ok: false, error: "not_positive" };
  }
  if (trimFraction(value.toFixed(QUANTITY_DECIMALS)) !== normalized) {
    return { ok: false, error: "precision" };
  }
  return { ok: true, value, normalized };
}

/** Options of {@link formatQuantity}. */
export interface FormatQuantityOptions {
  /** Minimum decimals to show. Default 0. */
  minDecimals?: number;
  /** Maximum decimals to show (≤ 6). Default 6. */
  maxDecimals?: number;
  /** Group thousands with `,`. Default `true`. */
  grouping?: boolean;
}

/**
 * Formats a quantity with `,` thousands separators and up to 6 decimals, without trailing zeros.
 * Returns `-` for a missing or non-finite value.
 */
export function formatQuantity(value: number | null | undefined, options: FormatQuantityOptions = {}): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return "-";
  }
  const maxDecimals = Math.min(Math.max(options.maxDecimals ?? QUANTITY_DECIMALS, 0), QUANTITY_DECIMALS);
  const minDecimals = Math.min(Math.max(options.minDecimals ?? 0, 0), maxDecimals);
  const fixed = Math.abs(value).toFixed(maxDecimals);
  const [integerPart = "0", rawFraction = ""] = fixed.split(".");
  let fraction = rawFraction.replace(/0+$/, "");
  if (fraction.length < minDecimals) {
    fraction = fraction.padEnd(minDecimals, "0");
  }
  const integer = options.grouping === false ? integerPart : integerPart.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  const isZero = /^0*$/.test(integerPart) && /^0*$/.test(fraction);
  const sign = value < 0 && !isZero ? "-" : "";
  return `${sign}${integer}${fraction ? `.${fraction}` : ""}`;
}

/** Text for an input field: no grouping, no trailing zeros; empty for a missing value. */
export function quantityToInput(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return "";
  }
  return formatQuantity(value, { grouping: false });
}

/** Formats money with 2 decimals and thousands separators. */
export function formatMoney(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return "-";
  }
  const rounded = Math.round((Math.abs(value) + Number.EPSILON) * 100) / 100;
  const text = formatQuantity(rounded, { minDecimals: 2, maxDecimals: 2 });
  return value < 0 && rounded !== 0 ? `-${text}` : text;
}
