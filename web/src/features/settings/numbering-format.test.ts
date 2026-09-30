import { describe, expect, it } from "vitest";

import { formatDocumentNumber } from "./numbering-format";

describe("formatDocumentNumber", () => {
  it("matches the example of the spec: PO-2610-0001", () => {
    expect(formatDocumentNumber({ prefix: "PO", period: "Month", digits: 4, separator: "-" }, "2026-10-05", 1)).toBe("PO-2610-0001");
  });

  it("uses the Gregorian year, not the Buddhist-era year", () => {
    expect(formatDocumentNumber({ prefix: "PR", period: "Year", digits: 5, separator: "/" }, "2026-09-29", 42)).toBe("PR/26/00042");
  });

  it("omits the period part when numbering never restarts", () => {
    expect(formatDocumentNumber({ prefix: "LOT", period: "None", digits: 6, separator: "" }, "2026-09-29", 7)).toBe("LOT000007");
    expect(formatDocumentNumber({ prefix: "LOT", period: "None", digits: 3, separator: "-" }, "2026-09-29", 7)).toBe("LOT-007");
  });

  it("upper-cases the prefix and does not cut long running numbers", () => {
    expect(formatDocumentNumber({ prefix: "gr", period: "Month", digits: 3, separator: "-" }, "2027-01-31", 12345)).toBe("GR-2701-12345");
  });

  it("stays printable while the form holds incomplete values", () => {
    expect(formatDocumentNumber({ prefix: "", period: "Month", digits: Number.NaN, separator: "-" }, "2026-09-29", 1)).toBe("-2609-1");
    expect(formatDocumentNumber({ prefix: "PO", period: "Month", digits: 4, separator: "-" }, "invalid", 1)).toBe("PO-0001");
  });
});
