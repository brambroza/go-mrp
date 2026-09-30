import { Permission, hasAnyPermission } from "@/lib/permissions";

/** Key of a home tile. */
export type HomeTileKey = "approvals" | "receive" | "issue" | "transfer" | "count" | "lotLookup" | "queue";

/** Tile on the home screen. */
export interface HomeTile {
  key: HomeTileKey;
  /** Route opened by the tile. */
  route: "/approvals" | "/receive" | "/issue" | "/transfer" | "/count" | "/lot-lookup" | "/queue";
  /** i18n key of the title. */
  titleKey: string;
  /** Short symbol shown on the tile. */
  symbol: string;
  /** The tile is shown when at least one of these is granted; empty = every signed-in user. */
  permissions: readonly string[];
}

/** All tiles in display order. */
export const HOME_TILES: readonly HomeTile[] = [
  { key: "approvals", route: "/approvals", titleKey: "home.tiles.approvals", symbol: "✓", permissions: [] },
  { key: "receive", route: "/receive", titleKey: "home.tiles.receive", symbol: "↓", permissions: [Permission.inventoryReceive] },
  { key: "issue", route: "/issue", titleKey: "home.tiles.issue", symbol: "↑", permissions: [Permission.inventoryIssue] },
  { key: "transfer", route: "/transfer", titleKey: "home.tiles.transfer", symbol: "⇄", permissions: [Permission.inventoryTransfer] },
  { key: "count", route: "/count", titleKey: "home.tiles.count", symbol: "#", permissions: [Permission.inventoryAdjust] },
  { key: "lotLookup", route: "/lot-lookup", titleKey: "home.tiles.lotLookup", symbol: "⌕", permissions: [Permission.inventoryRead] },
  {
    key: "queue",
    route: "/queue",
    titleKey: "home.tiles.queue",
    symbol: "⇪",
    permissions: [Permission.inventoryReceive, Permission.inventoryIssue, Permission.inventoryTransfer, Permission.inventoryAdjust],
  },
];

/** Tiles the user may see, in display order. */
export function visibleTiles(permissions: readonly string[] | null | undefined): HomeTile[] {
  return HOME_TILES.filter((tile) => hasAnyPermission(permissions, tile.permissions));
}

/** Returns whether the user may open the feature of a tile (used as a route guard). */
export function canOpenTile(permissions: readonly string[] | null | undefined, key: HomeTileKey): boolean {
  const tile = HOME_TILES.find((candidate) => candidate.key === key);
  return tile !== undefined && hasAnyPermission(permissions, tile.permissions);
}
