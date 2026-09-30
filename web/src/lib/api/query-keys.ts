/**
 * Roots of the TanStack Query keys, one per API resource. A list query uses
 * `[root, "list", params]`, a detail query `[root, "detail", id]`; invalidating `[root]` refreshes both.
 * Add the roots of a new module here.
 */
export const queryKeys = {
  units: ["masters", "units"],
  unitConversions: ["masters", "unit-conversions"],
  itemGroups: ["masters", "item-groups"],
  items: ["masters", "items"],
  warehouses: ["masters", "warehouses"],
  locations: ["masters", "locations"],
  customers: ["masters", "customers"],
  suppliers: ["masters", "suppliers"],
  supplierPrices: ["masters", "supplier-prices"],
  approvals: ["platform", "approvals"],
  approvalRoutes: ["platform", "approval-routes"],
  users: ["platform", "users"],
  roles: ["platform", "roles"],
  permissions: ["platform", "permissions"],
  settings: ["platform", "settings"],
  numbering: ["platform", "document-number-formats"],
  dashboard: ["dashboard"],
} as const satisfies Record<string, readonly string[]>;
