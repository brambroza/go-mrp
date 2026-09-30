import type { Schemas } from "@mrp/api-client";

import { getApi } from "./client";
import { unwrap } from "./http";

/** Purchase order list row. */
export type PurchaseOrderSummary = Schemas["PurchaseOrderSummary"];
/** Purchase order with lines. */
export type PurchaseOrder = Schemas["PurchaseOrderDto"];
/** Purchase order line with receiving progress. */
export type PurchaseOrderLine = Schemas["PurchaseOrderLineDto"];

/** Purchase orders that can still be received, optionally filtered by document number. */
export async function fetchReceivableOrders(search: string): Promise<PurchaseOrderSummary[]> {
  const page = await unwrap(
    getApi().GET("/api/v1/purchasing/orders", {
      params: { query: { receivableOnly: true, search: search.trim() || undefined, page: 1, pageSize: 50 } },
    }),
  );
  return page.items;
}

/** One purchase order with lines. */
export function fetchOrder(id: string): Promise<PurchaseOrder> {
  return unwrap(getApi().GET("/api/v1/purchasing/orders/{id}", { params: { path: { id } } }));
}
