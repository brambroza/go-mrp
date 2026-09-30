import type { Schemas } from "@mrp/api-client";

import { getApi } from "./client";
import { unwrap } from "./http";

/** Item master row. */
export type Item = Schemas["ItemDto"];
/** Warehouse master row. */
export type Warehouse = Schemas["WarehouseDto"];
/** Location master row. */
export type Location = Schemas["LocationDto"];

/** Largest page the API returns. */
const MAX_PAGE_SIZE = 200;

/** Searches active items by code, name or barcode. */
export async function searchItems(search: string, pageSize = 30): Promise<Item[]> {
  const page = await unwrap(
    getApi().GET("/api/v1/masters/items", { params: { query: { search: search.trim(), activeOnly: true, page: 1, pageSize } } }),
  );
  return page.items;
}

/** Active warehouses. */
export async function fetchWarehouses(): Promise<Warehouse[]> {
  const page = await unwrap(
    getApi().GET("/api/v1/masters/warehouses", { params: { query: { activeOnly: true, page: 1, pageSize: MAX_PAGE_SIZE } } }),
  );
  return page.items;
}

/**
 * Active locations of a warehouse. The API has no warehouse filter on this list, so pages are
 * read (at most 5 × 200 rows) and filtered on the device.
 */
export async function fetchLocations(warehouseId: string, search?: string): Promise<Location[]> {
  const found: Location[] = [];
  for (let page = 1; page <= 5; page += 1) {
    const result = await unwrap(
      getApi().GET("/api/v1/masters/locations", {
        params: { query: { search: search?.trim() || undefined, activeOnly: true, page, pageSize: MAX_PAGE_SIZE } },
      }),
    );
    found.push(...result.items.filter((location) => location.warehouseId === warehouseId));
    if (page * MAX_PAGE_SIZE >= result.total) {
      break;
    }
  }
  return found;
}
