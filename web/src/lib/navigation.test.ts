import { describe, expect, it } from "vitest";

import { hasPermission, Permissions } from "@/lib/auth/permissions";

import { isActivePath, navigation, planIncludes, visibleNavigation } from "./navigation";

describe("visibleNavigation", () => {
  const forUser = (granted: string[]) => visibleNavigation(navigation, (permission) => hasPermission(granted, permission));

  it("shows every group to the owner", () => {
    expect(forUser(["*"]).map((group) => group.id)).toEqual(navigation.map((group) => group.id));
  });

  it("hides entries and whole groups the user has no permission for", () => {
    const groups = forUser([Permissions.mastersRead, Permissions.usersManage]);
    expect(groups.map((group) => group.id)).toEqual(["overview", "masters", "approvals", "settings"]);
    expect(groups.find((group) => group.id === "settings")?.items.map((item) => item.href)).toEqual(["/settings/users"]);
  });

  it("always shows the dashboard and the approval inbox", () => {
    expect(forUser([]).map((group) => group.id)).toEqual(["overview", "approvals"]);
  });

  it("has unique routes", () => {
    const routes = navigation.flatMap((group) => group.items.map((item) => item.href));
    expect(new Set(routes).size).toBe(routes.length);
  });
});

describe("planIncludes", () => {
  it("compares packages by rank", () => {
    expect(planIncludes("Starter", "Pro")).toBe(false);
    expect(planIncludes("Pro", "Pro")).toBe(true);
    expect(planIncludes("Enterprise", "Pro")).toBe(true);
    expect(planIncludes("Starter", undefined)).toBe(true);
    expect(planIncludes("Unknown", "Starter")).toBe(false);
    expect(planIncludes(null, "Starter")).toBe(false);
  });
});

describe("isActivePath", () => {
  it("marks the entry of the current page", () => {
    expect(isActivePath("/", "/")).toBe(true);
    expect(isActivePath("/masters/items", "/")).toBe(false);
    expect(isActivePath("/approvals/123", "/approvals")).toBe(true);
    expect(isActivePath("/masters/item-groups", "/masters/items")).toBe(false);
  });
});
