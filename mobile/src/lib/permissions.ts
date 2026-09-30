/** Permission codes of the API used by the mobile app. */
export const Permission = {
  inventoryRead: "inventory.read",
  inventoryReceive: "inventory.receive",
  inventoryIssue: "inventory.issue",
  inventoryTransfer: "inventory.transfer",
  inventoryAdjust: "inventory.adjust",
  mastersRead: "masters.read",
  purchasingRead: "purchasing.read",
} as const;

/** Wildcard granted to the system role `Owner`. */
export const ALL_PERMISSIONS = "*";

/** Returns whether the granted list contains the permission (or the wildcard). */
export function hasPermission(granted: readonly string[] | null | undefined, required: string): boolean {
  if (!granted || granted.length === 0) {
    return false;
  }
  return granted.includes(ALL_PERMISSIONS) || granted.includes(required);
}

/** Returns whether at least one of the permissions is granted. An empty list means "any signed-in user". */
export function hasAnyPermission(granted: readonly string[] | null | undefined, required: readonly string[]): boolean {
  if (required.length === 0) {
    return true;
  }
  return required.some((permission) => hasPermission(granted, permission));
}
