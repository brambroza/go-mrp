import { describe, expect, it } from "vitest";

import { safeNextPath } from "./safe-redirect";

describe("safeNextPath", () => {
  it("keeps paths of this site", () => {
    expect(safeNextPath("/masters/items")).toBe("/masters/items");
    expect(safeNextPath("/approvals/123?tab=mine")).toBe("/approvals/123?tab=mine");
  });

  it("refuses to leave the site", () => {
    for (const next of ["https://evil.example", "//evil.example", "/\\evil.example", "javascript:alert(1)", "evil.example", "/\nfoo", ""]) {
      expect(safeNextPath(next), JSON.stringify(next)).toBe("/");
    }
    expect(safeNextPath(null)).toBe("/");
    expect(safeNextPath(undefined, "/home")).toBe("/home");
  });

  it("does not loop back to the sign-in pages", () => {
    expect(safeNextPath("/login")).toBe("/");
    expect(safeNextPath("/login?next=/x")).toBe("/");
    expect(safeNextPath("/signup")).toBe("/");
  });
});
