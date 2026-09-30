import {
  BadgeCheckIcon,
  BoxesIcon,
  BuildingIcon,
  CalendarCheckIcon,
  ClipboardListIcon,
  FactoryIcon,
  FileTextIcon,
  FlaskConicalIcon,
  GaugeIcon,
  HashIcon,
  LayersIcon,
  ListTreeIcon,
  PackageIcon,
  RulerIcon,
  ScrollTextIcon,
  SettingsIcon,
  ShieldIcon,
  ShoppingCartIcon,
  TruckIcon,
  UsersIcon,
  WarehouseIcon,
  WorkflowIcon,
  type LucideIcon,
} from "lucide-react";

import { Permissions, type PermissionRequirement } from "@/lib/auth/permissions";

/** Key of a label in `messages/<locale>/nav.json`. */
export type NavLabelKey =
  | `groups.${"overview" | "masters" | "purchasing" | "inventory" | "production" | "approvals" | "settings"}`
  | `items.${
      | "dashboard"
      | "items"
      | "units"
      | "itemGroups"
      | "warehouses"
      | "customers"
      | "suppliers"
      | "purchaseRequests"
      | "purchaseOrders"
      | "stockDocuments"
      | "onHand"
      | "stockCard"
      | "lots"
      | "periods"
      | "boms"
      | "mrp"
      | "workOrders"
      | "approvals"
      | "users"
      | "roles"
      | "approvalRoutes"
      | "numbering"
      | "general"}`;

/** Live counters that can be shown as a badge on a menu entry. */
export type NavBadge = "pendingApprovals";

/** One entry of the sidebar. */
export interface NavItem {
  /** Route of the page. */
  href: string;
  /** Translation key of the label. */
  label: NavLabelKey;
  /** Icon. */
  icon: LucideIcon;
  /** Permission needed to see the entry; an array means "any of these". */
  permission?: PermissionRequirement;
  /** Counter shown next to the label. */
  badge?: NavBadge;
}

/** Tenant packages, lowest first. */
export const planOrder = ["Starter", "Pro", "Enterprise"] as const;

/** A tenant package. */
export type Plan = (typeof planOrder)[number];

/** A group of sidebar entries, one per module. */
export interface NavGroup {
  /** Stable id of the module. */
  id: string;
  /** Translation key of the group heading. */
  label: NavLabelKey;
  /** Entries of the group. */
  items: NavItem[];
  /** Lowest package that includes the module. */
  minPlan?: Plan;
  /** The API of the module does not exist yet: entries are shown greyed out and cannot be opened. */
  comingSoon?: boolean;
}

/**
 * The sidebar. To add a module: add its permission codes to `Permissions`, add a group here, add
 * the labels to `nav.json`, and create the pages under `src/app/(app)/<module>/`.
 */
export const navigation: NavGroup[] = [
  {
    id: "overview",
    label: "groups.overview",
    items: [{ href: "/", label: "items.dashboard", icon: GaugeIcon }],
  },
  {
    id: "masters",
    label: "groups.masters",
    items: [
      { href: "/masters/items", label: "items.items", icon: PackageIcon, permission: Permissions.mastersRead },
      { href: "/masters/units", label: "items.units", icon: RulerIcon, permission: Permissions.mastersRead },
      { href: "/masters/item-groups", label: "items.itemGroups", icon: LayersIcon, permission: Permissions.mastersRead },
      { href: "/masters/warehouses", label: "items.warehouses", icon: WarehouseIcon, permission: Permissions.mastersRead },
      { href: "/masters/customers", label: "items.customers", icon: BuildingIcon, permission: Permissions.mastersRead },
      { href: "/masters/suppliers", label: "items.suppliers", icon: TruckIcon, permission: Permissions.mastersRead },
    ],
  },
  {
    id: "purchasing",
    label: "groups.purchasing",
    items: [
      { href: "/purchasing/requests", label: "items.purchaseRequests", icon: ClipboardListIcon, permission: Permissions.purchasingRead },
      { href: "/purchasing/orders", label: "items.purchaseOrders", icon: ShoppingCartIcon, permission: Permissions.purchasingRead },
    ],
  },
  {
    id: "inventory",
    label: "groups.inventory",
    items: [
      { href: "/inventory/documents", label: "items.stockDocuments", icon: FileTextIcon, permission: Permissions.inventoryRead },
      { href: "/inventory/on-hand", label: "items.onHand", icon: BoxesIcon, permission: Permissions.inventoryRead },
      { href: "/inventory/stock-card", label: "items.stockCard", icon: ScrollTextIcon, permission: Permissions.inventoryRead },
      { href: "/inventory/lots", label: "items.lots", icon: FlaskConicalIcon, permission: Permissions.inventoryRead },
      { href: "/inventory/periods", label: "items.periods", icon: CalendarCheckIcon, permission: Permissions.inventoryClosePeriod },
    ],
  },
  {
    id: "production",
    label: "groups.production",
    minPlan: "Pro",
    comingSoon: true,
    items: [
      { href: "/production/boms", label: "items.boms", icon: ListTreeIcon, permission: Permissions.productionRead },
      { href: "/production/mrp", label: "items.mrp", icon: WorkflowIcon, permission: Permissions.productionRead },
      { href: "/production/work-orders", label: "items.workOrders", icon: FactoryIcon, permission: Permissions.productionRead },
    ],
  },
  {
    id: "approvals",
    label: "groups.approvals",
    items: [{ href: "/approvals", label: "items.approvals", icon: BadgeCheckIcon, badge: "pendingApprovals" }],
  },
  {
    id: "settings",
    label: "groups.settings",
    items: [
      { href: "/settings/users", label: "items.users", icon: UsersIcon, permission: Permissions.usersManage },
      { href: "/settings/roles", label: "items.roles", icon: ShieldIcon, permission: Permissions.rolesManage },
      { href: "/settings/approval-routes", label: "items.approvalRoutes", icon: WorkflowIcon, permission: Permissions.approvalsConfigure },
      { href: "/settings/numbering", label: "items.numbering", icon: HashIcon, permission: Permissions.settingsManage },
      { href: "/settings/general", label: "items.general", icon: SettingsIcon, permission: Permissions.settingsManage },
    ],
  },
];

/** Whether the tenant's package includes a module that needs `minPlan`. */
export function planIncludes(plan: string | null | undefined, minPlan: Plan | undefined): boolean {
  if (!minPlan) {
    return true;
  }
  const current = planOrder.indexOf(plan as Plan);
  return current >= 0 && current >= planOrder.indexOf(minPlan);
}

/** Whether a menu entry is the current page (the dashboard only on an exact match). */
export function isActivePath(pathname: string, href: string): boolean {
  return href === "/" ? pathname === "/" : pathname === href || pathname.startsWith(`${href}/`);
}

/**
 * The groups and entries a user may see: entries without the permission are removed and groups
 * without entries disappear.
 */
export function visibleNavigation(groups: readonly NavGroup[], can: (permission: PermissionRequirement | undefined) => boolean): NavGroup[] {
  return groups
    .map((group) => ({ ...group, items: group.items.filter((item) => can(item.permission)) }))
    .filter((group) => group.items.length > 0);
}
