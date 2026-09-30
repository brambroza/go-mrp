import { z } from "zod";

import { parseIsoDate } from "@/lib/date";
import { MAX_QUANTITY } from "@/lib/quantity";
import { MAX_USER_REMARK } from "@/offline/sender";

/**
 * Client-side validation with the same limits as the API contracts
 * (`api/src/Modules/*\/Application/Contracts.cs`). Messages are i18n keys.
 */

/** Removes control characters and surrounding white space from typed or scanned text. */
export function cleanText(text: string): string {
  return text.replace(/[\u0000-\u001f\u007f]/g, "").trim();
}

const uuid = z.string().regex(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i, "validation.invalid");

const isoDate = z.string().refine((value) => parseIsoDate(value) !== null, "validation.date_invalid");

/** Sign-in form (`LoginRequest`). */
export const loginSchema = z.object({
  companyCode: z
    .string()
    .transform(cleanText)
    .pipe(z.string().min(1, "validation.required").min(3, "validation.company_code_length").max(40, "validation.company_code_length")),
  userName: z.string().transform(cleanText).pipe(z.string().min(1, "validation.required").max(200, "validation.too_long")),
  password: z.string().min(1, "validation.required").max(100, "validation.too_long"),
});

/** TOTP code (`LoginRequest.TwoFactorCode`): 6 to 10 characters; spaces are ignored. */
export const twoFactorCodeSchema = z
  .string()
  .transform((value) => value.replace(/\s/g, ""))
  .pipe(z.string().min(1, "validation.required").regex(/^\d{6,10}$/, "validation.two_factor_format"));

/** Optional remark of an approval (`ApprovalDecisionRequest`). */
export const approveCommentSchema = z.string().transform(cleanText).pipe(z.string().max(1000, "validation.too_long"));

/** Reason of a rejection: required. */
export const rejectCommentSchema = z
  .string()
  .transform(cleanText)
  .pipe(z.string().min(1, "validation.reject_reason_required").max(1000, "validation.too_long"));

/** Text from the scanner or typed by hand: a barcode is short. */
export const scannedTextSchema = z
  .string()
  .transform(cleanText)
  .pipe(z.string().min(1, "validation.required").max(200, "validation.too_long"));

/** Lot number (`SaveStockDocumentLine.LotNo`). */
export const lotNoSchema = z
  .string()
  .transform(cleanText)
  .pipe(z.string().max(40, "validation.too_long").regex(/^[A-Za-z0-9._/-]*$/, "validation.lot_no_format"));

/** Line of a warehouse document (`SaveStockDocumentLine`). */
export const documentLineSchema = z
  .object({
    itemId: uuid,
    quantity: z.number().min(-MAX_QUANTITY, "validation.quantity_range").max(MAX_QUANTITY, "validation.quantity_range"),
    unitId: uuid.nullish(),
    locationId: uuid.nullish(),
    toLocationId: uuid.nullish(),
    lotId: uuid.nullish(),
    lotNo: z
      .string()
      .max(40, "validation.too_long")
      .regex(/^[A-Za-z0-9._/-]*$/, "validation.lot_no_format")
      .nullish(),
    supplierLot: z.string().max(60, "validation.too_long").nullish(),
    mfgDate: isoDate.nullish(),
    expiryDate: isoDate.nullish(),
    poLineId: uuid.nullish(),
    reasonCode: z
      .string()
      .max(20, "validation.too_long")
      .regex(/^[A-Za-z0-9_-]*$/, "validation.invalid")
      .nullish(),
    remark: z.string().max(300, "validation.too_long").nullish(),
  })
  .refine((line) => !line.mfgDate || !line.expiryDate || line.expiryDate >= line.mfgDate, {
    message: "validation.expiry_before_mfg",
    path: ["expiryDate"],
  });

/** Warehouse document (`SaveStockDocumentRequest`). */
export const documentSchema = z
  .object({
    documentType: z.enum(["Receipt", "Issue", "Transfer", "Adjustment", "Count", "SupplierReturn"]),
    documentDate: isoDate,
    warehouseId: uuid,
    toWarehouseId: uuid.nullish(),
    supplierId: uuid.nullish(),
    referenceType: z.string().max(16, "validation.too_long").nullish(),
    referenceId: uuid.nullish(),
    referenceNo: z.string().max(40, "validation.too_long").nullish(),
    remark: z.string().max(MAX_USER_REMARK, "validation.too_long").nullish(),
    lines: z.array(documentLineSchema).min(1, "validation.lines_required").max(500, "validation.too_many_lines"),
  })
  .superRefine((document, context) => {
    document.lines.forEach((line, index) => {
      const positiveOnly = document.documentType !== "Adjustment" && document.documentType !== "Count";
      if (positiveOnly && line.quantity <= 0) {
        context.addIssue({ code: "custom", message: "validation.quantity_positive", path: ["lines", index, "quantity"] });
      }
      if (document.documentType === "Count" && line.quantity < 0) {
        context.addIssue({ code: "custom", message: "validation.quantity_not_negative", path: ["lines", index, "quantity"] });
      }
    });
    if (document.documentType === "Transfer" && !document.toWarehouseId) {
      context.addIssue({ code: "custom", message: "validation.to_warehouse_required", path: ["toWarehouseId"] });
    }
  });

/** Count sheet request (`CreateCountSheetRequest`). */
export const countSheetSchema = z.object({
  documentDate: isoDate,
  warehouseId: uuid,
  locationId: uuid.nullish(),
  remark: z.string().max(500, "validation.too_long").nullish(),
});

/** First validation message (an i18n key) of a failed parse, with the path of the field. */
export function firstIssue(error: z.ZodError): { path: string; message: string } {
  const issue = error.issues[0];
  return { path: issue ? issue.path.join(".") : "", message: issue?.message ?? "validation.invalid" };
}
