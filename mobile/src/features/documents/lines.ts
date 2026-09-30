import { parseQuantity, type QuantityError } from "@/lib/quantity";

/** Quantity text of a form line with its parse result. */
export interface QuantityInput {
  text: string;
  value: number | null;
  error: QuantityError | null;
}

/** Parses the text of a quantity field. An empty optional field has no value and no error. */
export function readQuantity(text: string, options: { required: boolean; allowZero?: boolean }): QuantityInput {
  const parsed = parseQuantity(text, { allowZero: options.allowZero });
  if (parsed.ok) {
    return { text, value: parsed.value, error: null };
  }
  if (parsed.error === "empty" && !options.required) {
    return { text, value: null, error: null };
  }
  return { text, value: null, error: parsed.error };
}

/** Rounds to 6 decimals to remove floating point noise from a division. */
export function roundQuantity(value: number): number {
  return Math.round((value + Number.EPSILON) * 1_000_000) / 1_000_000;
}

/** Outstanding quantity of a PO line in the unit of the PO (the API reports it in stock units). */
export function outstandingInOrderUnit(outstandingStockQuantity: number, conversionFactor: number): number {
  if (!Number.isFinite(conversionFactor) || conversionFactor <= 0) {
    return roundQuantity(outstandingStockQuantity);
  }
  return roundQuantity(outstandingStockQuantity / conversionFactor);
}

/** Returns an empty text as `null` and trims the rest; optional text fields of the API are nullable. */
export function textOrNull(text: string): string | null {
  const trimmed = text.trim();
  return trimmed === "" ? null : trimmed;
}
