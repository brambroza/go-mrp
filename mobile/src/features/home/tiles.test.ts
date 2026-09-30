import { hasAnyPermission, hasPermission } from "@/lib/permissions";

import { canOpenTile, HOME_TILES, visibleTiles } from "./tiles";

const keys = (permissions: readonly string[] | null | undefined) => visibleTiles(permissions).map((tile) => tile.key);

describe("permission filtering of home tiles", () => {
  it("shows every tile to a user with the wildcard", () => {
    expect(keys(["*"])).toEqual(["approvals", "receive", "issue", "transfer", "count", "lotLookup", "queue"]);
    expect(visibleTiles(["*"])).toHaveLength(HOME_TILES.length);
  });

  it("shows only the approval inbox to a user without warehouse permissions", () => {
    expect(keys([])).toEqual(["approvals"]);
    expect(keys(null)).toEqual(["approvals"]);
    expect(keys(["purchasing.po.approve", "purchasing.read"])).toEqual(["approvals"]);
  });

  it("shows receiving and the queue to a receiving clerk", () => {
    expect(keys(["inventory.receive", "inventory.read"])).toEqual(["approvals", "receive", "lotLookup", "queue"]);
  });

  it("shows each warehouse tile only with its own permission", () => {
    expect(keys(["inventory.issue"])).toEqual(["approvals", "issue", "queue"]);
    expect(keys(["inventory.transfer"])).toEqual(["approvals", "transfer", "queue"]);
    expect(keys(["inventory.adjust"])).toEqual(["approvals", "count", "queue"]);
    expect(keys(["inventory.read"])).toEqual(["approvals", "lotLookup"]);
  });

  it("does not match partial or differently cased codes", () => {
    expect(keys(["inventory"])).toEqual(["approvals"]);
    expect(keys(["inventory.receive.extra"])).toEqual(["approvals"]);
    expect(keys(["INVENTORY.RECEIVE"])).toEqual(["approvals"]);
    expect(keys(["inventory.*"])).toEqual(["approvals"]);
  });

  it("guards routes with the same rule", () => {
    expect(canOpenTile(["inventory.issue"], "issue")).toBe(true);
    expect(canOpenTile(["inventory.issue"], "receive")).toBe(false);
    expect(canOpenTile([], "approvals")).toBe(true);
    expect(canOpenTile(["*"], "count")).toBe(true);
  });
});

describe("permission helpers", () => {
  it("checks one permission", () => {
    expect(hasPermission(["a.b"], "a.b")).toBe(true);
    expect(hasPermission(["a.b"], "a.c")).toBe(false);
    expect(hasPermission(["*"], "anything")).toBe(true);
    expect(hasPermission(undefined, "a.b")).toBe(false);
  });

  it("treats an empty requirement as allowed", () => {
    expect(hasAnyPermission([], [])).toBe(true);
    expect(hasAnyPermission(["x"], ["y", "x"])).toBe(true);
    expect(hasAnyPermission(["x"], ["y", "z"])).toBe(false);
  });
});
