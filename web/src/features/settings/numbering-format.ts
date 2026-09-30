import { parseIsoDate } from "@/lib/format/date";

/** Periods after which the running number starts again. */
export const numberingPeriods = ["None", "Year", "Month"] as const;

/** A numbering period. */
export type NumberingPeriod = (typeof numberingPeriods)[number];

/** Separators the API accepts between the parts of a document number. */
export const separators = ["-", "/", ""] as const;

/** Parts of a document number format. */
export interface DocumentNumberFormat {
  prefix: string;
  period: NumberingPeriod;
  digits: number;
  separator: string;
}

/**
 * Builds a document number the way the API does (`DocumentNumberFormat.Format`):
 * `PREFIX + separator + period + separator + running`, where the period is `yy` or `yyMM`
 * of the Gregorian year — `PO-2610-0001` for October 2026 — and the running number is padded
 * with zeros to `digits`. `date` is a `YYYY-MM-DD` date in the tenant's time zone.
 */
export function formatDocumentNumber(format: DocumentNumberFormat, date: string, running: number): string {
  const parts = [format.prefix.trim().toUpperCase()];
  const day = parseIsoDate(date);
  if (day && format.period !== "None") {
    const year = String(day.year % 100).padStart(2, "0");
    parts.push(format.period === "Month" ? `${year}${String(day.month).padStart(2, "0")}` : year);
  }
  const digits = Number.isInteger(format.digits) ? Math.min(Math.max(format.digits, 1), 18) : 1;
  parts.push(String(Math.max(0, Math.trunc(running))).padStart(digits, "0"));
  return parts.join(format.separator);
}
