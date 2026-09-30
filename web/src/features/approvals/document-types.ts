"use client";

import { useTranslations } from "next-intl";

/**
 * Document type keys of the API (`api/src/Mrp.SharedKernel/Domain/DocumentTypes.cs`).
 * Add the key and its label in `common.json` → `documentTypes` when a module adds a document.
 */
export const documentTypes = ["PR", "PO", "GR", "GI", "TF", "ADJ", "CNT", "RTS", "LOT", "SO", "WO", "MRP"] as const;

/** A document type key the web app knows. */
export type DocumentType = (typeof documentTypes)[number];

/** Keys that only number things and never go through approval. */
export const numberingOnlyTypes: readonly string[] = ["LOT", "MRP"];

/** Whether the key is a document type with a translated label. */
export function isDocumentType(value: string): value is DocumentType {
  return (documentTypes as readonly string[]).includes(value);
}

/** Returns a function that gives the label of a document type, for example `PO` → "Purchase order". */
export function useDocumentTypeLabel(): (documentType: string) => string {
  const t = useTranslations("common");
  return (documentType) => (isDocumentType(documentType) ? t(`documentTypes.${documentType}`) : documentType);
}
