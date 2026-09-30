import { describe, expect, it } from "vitest";

import { ALL_PERMISSIONS, Permissions, hasAllPermissions, hasPermission } from "./permissions";

describe("hasPermission", () => {
  const granted = [Permissions.mastersRead, Permissions.purchasingRead];

  it("grants a code the user has", () => {
    expect(hasPermission(granted, Permissions.mastersRead)).toBe(true);
  });

  it("denies a code the user does not have", () => {
    expect(hasPermission(granted, Permissions.mastersManage)).toBe(false);
  });

  it("does not treat a code as a prefix of another", () => {
    expect(hasPermission(["masters"], Permissions.mastersRead)).toBe(false);
    expect(hasPermission([Permissions.inventoryRead], "inventory")).toBe(false);
    expect(hasPermission(["masters.*"], Permissions.mastersRead)).toBe(false);
  });

  it("is case sensitive, like the API", () => {
    expect(hasPermission(["Masters.Read"], Permissions.mastersRead)).toBe(false);
  });

  it("grants everything to the wildcard", () => {
    expect(hasPermission([ALL_PERMISSIONS], Permissions.usersManage)).toBe(true);
    expect(hasPermission([ALL_PERMISSIONS], "module.that.does.not.exist.yet")).toBe(true);
  });

  it("treats an array as any-of", () => {
    expect(hasPermission(granted, [Permissions.mastersManage, Permissions.purchasingRead])).toBe(true);
    expect(hasPermission(granted, [Permissions.mastersManage, Permissions.usersManage])).toBe(false);
  });

  it("needs nothing when nothing is required", () => {
    expect(hasPermission([], undefined)).toBe(true);
    expect(hasPermission(null, null)).toBe(true);
    expect(hasPermission(undefined, [])).toBe(true);
  });

  it("denies when the user has no permissions", () => {
    expect(hasPermission([], Permissions.mastersRead)).toBe(false);
    expect(hasPermission(null, Permissions.mastersRead)).toBe(false);
    expect(hasPermission(undefined, [Permissions.mastersRead])).toBe(false);
  });
});

describe("hasAllPermissions", () => {
  it("needs every code", () => {
    const granted = [Permissions.mastersRead, Permissions.mastersManage];
    expect(hasAllPermissions(granted, [Permissions.mastersRead, Permissions.mastersManage])).toBe(true);
    expect(hasAllPermissions(granted, [Permissions.mastersRead, Permissions.usersManage])).toBe(false);
    expect(hasAllPermissions([ALL_PERMISSIONS], [Permissions.mastersRead, Permissions.usersManage])).toBe(true);
    expect(hasAllPermissions([], [])).toBe(true);
  });
});

describe("Permissions", () => {
  it("matches the catalogue of the API", async () => {
    const { readFile } = await import("node:fs/promises");
    const source = await readFile(new URL("../../../../api/src/Mrp.SharedKernel/Domain/Permissions.cs", import.meta.url), "utf8").catch(() => null);
    if (source === null) {
      // The web app is built without the API sources (Docker image); nothing to compare with.
      return;
    }
    const apiCodes = [...source.matchAll(/public const string \w+ = "([^"]+)";/g)].map((match) => match[1]).sort();
    expect(Object.values(Permissions).slice().sort()).toEqual(apiCodes);
  });
});
