/** Permission that grants everything (role `Owner`). */
export const ALL_PERMISSIONS = "*";

/**
 * Permission codes of the API (`api/src/Mrp.SharedKernel/Domain/Permissions.cs`).
 * Add the codes of a new module here when its API is published.
 */
export const Permissions = {
  usersManage: "platform.users.manage",
  rolesManage: "platform.roles.manage",
  settingsManage: "platform.settings.manage",
  approvalsConfigure: "platform.approvals.configure",
  mastersRead: "masters.read",
  mastersManage: "masters.manage",
  inventoryRead: "inventory.read",
  inventoryReceive: "inventory.receive",
  inventoryIssue: "inventory.issue",
  inventoryTransfer: "inventory.transfer",
  inventoryAdjust: "inventory.adjust",
  inventoryQc: "inventory.qc",
  inventoryClosePeriod: "inventory.period.close",
  purchasingRead: "purchasing.read",
  purchaseRequestManage: "purchasing.pr.manage",
  purchaseOrderManage: "purchasing.po.manage",
  productionRead: "production.read",
  bomManage: "production.bom.manage",
  mrpRun: "production.mrp.run",
  workOrderManage: "production.workorder.manage",
} as const;

/** A permission code known to the web app. */
export type PermissionCode = (typeof Permissions)[keyof typeof Permissions];

/** One code, or several of which any one is enough. */
export type PermissionRequirement = string | readonly string[];

/**
 * Whether the granted permissions satisfy the requirement. `"*"` grants everything; an array
 * requirement is satisfied by any of its codes; an empty requirement is always satisfied.
 */
export function hasPermission(
  granted: readonly string[] | null | undefined,
  required: PermissionRequirement | null | undefined,
): boolean {
  if (required === null || required === undefined) {
    return true;
  }
  const codes = typeof required === "string" ? [required] : required;
  if (codes.length === 0) {
    return true;
  }
  if (!granted || granted.length === 0) {
    return false;
  }
  if (granted.includes(ALL_PERMISSIONS)) {
    return true;
  }
  return codes.some((code) => granted.includes(code));
}

/** Whether every one of the codes is granted. */
export function hasAllPermissions(granted: readonly string[] | null | undefined, required: readonly string[]): boolean {
  return required.every((code) => hasPermission(granted, code));
}
