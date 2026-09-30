import type { Schemas } from "@mrp/api-client";

import { getApi } from "./client";
import { unwrap } from "./http";

/** Request body that creates or replaces a draft warehouse document. */
export type SaveStockDocument = Schemas["SaveStockDocumentRequest"];
/** Line of {@link SaveStockDocument}. */
export type SaveStockDocumentLine = Schemas["SaveStockDocumentLine"];
/** Warehouse document with lines. */
export type StockDocument = Schemas["StockDocumentDto"];
/** Type of a warehouse document. */
export type StockDocumentType = Schemas["StockDocumentType"];
/** Status of a warehouse document. */
export type StockDocumentStatus = Schemas["StockDocumentStatus"];
/** Lot with on-hand quantity. */
export type Lot = Schemas["LotDto"];
/** Lots the system would consume for a quantity. */
export type AllocationPreview = Schemas["AllocationPreviewDto"];
/** On-hand row. */
export type OnHandRow = Schemas["OnHandRow"];

/** Creates a draft document. */
export function createDocument(body: SaveStockDocument): Promise<StockDocument> {
  return unwrap(getApi().POST("/api/v1/inventory/documents", { body }));
}

/** Replaces header and lines of a draft. */
export function updateDocument(id: string, body: SaveStockDocument): Promise<StockDocument> {
  return unwrap(getApi().PUT("/api/v1/inventory/documents/{id}", { params: { path: { id } }, body }));
}

/** Posts stock; the result stays `Submitted` when the tenant requires approval. */
export function postDocument(id: string): Promise<StockDocument> {
  return unwrap(getApi().POST("/api/v1/inventory/documents/{id}/post", { params: { path: { id } } }));
}

/** Reads one document. */
export function fetchDocument(id: string): Promise<StockDocument> {
  return unwrap(getApi().GET("/api/v1/inventory/documents/{id}", { params: { path: { id } } }));
}

/**
 * Looks for a draft of the given type and date whose remark contains the tag. Used to find a
 * draft that was created by a request whose response never arrived.
 */
export async function findDraftByRemarkTag(type: StockDocumentType, documentDate: string, tag: string): Promise<{ id: string; documentNo: string } | null> {
  for (let page = 1; page <= 3; page += 1) {
    const result = await unwrap(
      getApi().GET("/api/v1/inventory/documents", {
        params: { query: { type, status: "Draft", from: documentDate, to: documentDate, page, pageSize: 200 } },
      }),
    );
    const match = result.items.find((row) => (row.remark ?? "").includes(tag));
    if (match) {
      return { id: match.id, documentNo: match.documentNo };
    }
    if (page * 200 >= result.total) {
      break;
    }
  }
  return null;
}

/** Creates a count sheet from the system balance (one line per lot and location). */
export function createCountSheet(body: Schemas["CreateCountSheetRequest"]): Promise<StockDocument> {
  return unwrap(getApi().POST("/api/v1/inventory/documents/count-sheet", { body }));
}

/** Looks up a lot by the number printed on its barcode. */
export function fetchLotByNumber(lotNo: string): Promise<Lot> {
  return unwrap(getApi().GET("/api/v1/inventory/lots/by-number/{lotNo}", { params: { path: { lotNo } } }));
}

/** Lots the system will consume (FIFO/FEFO) for the quantity. */
export function fetchAllocationPreview(itemId: string, warehouseId: string, quantity: number): Promise<AllocationPreview> {
  return unwrap(getApi().GET("/api/v1/inventory/allocation-preview", { params: { query: { itemId, warehouseId, quantity } } }));
}

/** On-hand rows of a lot, per warehouse and location. */
export function fetchOnHandOfLot(lotId: string): Promise<OnHandRow[]> {
  return unwrap(getApi().GET("/api/v1/inventory/on-hand", { params: { query: { lotId, page: 1, pageSize: 200 } } }));
}
