import { formatMoney, formatQuantity, normalizeDigits, parseQuantity, quantityToInput } from "./quantity";

describe("parseQuantity", () => {
  it("parses whole numbers and decimals", () => {
    expect(parseQuantity("12")).toEqual({ ok: true, value: 12, normalized: "12" });
    expect(parseQuantity("0.5")).toEqual({ ok: true, value: 0.5, normalized: "0.5" });
    expect(parseQuantity(".5")).toEqual({ ok: true, value: 0.5, normalized: "0.5" });
    expect(parseQuantity("7.")).toEqual({ ok: true, value: 7, normalized: "7" });
    expect(parseQuantity("007.500")).toEqual({ ok: true, value: 7.5, normalized: "7.5" });
  });

  it("accepts exactly 6 decimals and rejects 7", () => {
    expect(parseQuantity("1.123456")).toEqual({ ok: true, value: 1.123456, normalized: "1.123456" });
    expect(parseQuantity("0.000001")).toEqual({ ok: true, value: 0.000001, normalized: "0.000001" });
    expect(parseQuantity("1.1234567")).toEqual({ ok: false, error: "too_many_decimals" });
    // Trailing zeros are not significant decimals.
    expect(parseQuantity("1.1234560")).toEqual({ ok: true, value: 1.123456, normalized: "1.123456" });
  });

  it("accepts thousands separators, spaces and Thai digits", () => {
    expect(parseQuantity("1,234.5")).toEqual({ ok: true, value: 1234.5, normalized: "1234.5" });
    expect(parseQuantity(" 1 234 ")).toEqual({ ok: true, value: 1234, normalized: "1234" });
    expect(parseQuantity("๑๒.๕")).toEqual({ ok: true, value: 12.5, normalized: "12.5" });
    expect(normalizeDigits("๐๑๒๓๔๕๖๗๘๙")).toBe("0123456789");
  });

  it("rejects empty and malformed text", () => {
    expect(parseQuantity("")).toEqual({ ok: false, error: "empty" });
    expect(parseQuantity("   ")).toEqual({ ok: false, error: "empty" });
    expect(parseQuantity(null)).toEqual({ ok: false, error: "empty" });
    expect(parseQuantity("abc")).toEqual({ ok: false, error: "invalid" });
    expect(parseQuantity("1.2.3")).toEqual({ ok: false, error: "invalid" });
    expect(parseQuantity("1e5")).toEqual({ ok: false, error: "invalid" });
    expect(parseQuantity(".")).toEqual({ ok: false, error: "invalid" });
    expect(parseQuantity("-")).toEqual({ ok: false, error: "invalid" });
    expect(parseQuantity("Infinity")).toEqual({ ok: false, error: "invalid" });
  });

  it("requires a positive quantity unless zero or negative is allowed", () => {
    expect(parseQuantity("0")).toEqual({ ok: false, error: "not_positive" });
    expect(parseQuantity("0.000000")).toEqual({ ok: false, error: "not_positive" });
    expect(parseQuantity("-1")).toEqual({ ok: false, error: "not_positive" });
    expect(parseQuantity("0", { allowZero: true })).toEqual({ ok: true, value: 0, normalized: "0" });
    expect(parseQuantity("-0", { allowZero: true })).toEqual({ ok: true, value: 0, normalized: "0" });
    expect(parseQuantity("-2.5", { allowNegative: true })).toEqual({ ok: true, value: -2.5, normalized: "-2.5" });
  });

  it("enforces the range of the API", () => {
    expect(parseQuantity("999999999999")).toEqual({ ok: true, value: 999999999999, normalized: "999999999999" });
    expect(parseQuantity("1000000000000")).toEqual({ ok: false, error: "out_of_range" });
  });

  it("rejects values a JavaScript number cannot hold exactly to 6 decimals", () => {
    expect(parseQuantity("99999999999.123456")).toEqual({ ok: false, error: "precision" });
    expect(parseQuantity("123456789.123456")).toEqual({ ok: true, value: 123456789.123456, normalized: "123456789.123456" });
  });
});

describe("formatQuantity", () => {
  it("groups thousands and trims trailing zeros", () => {
    expect(formatQuantity(1234567.5)).toBe("1,234,567.5");
    expect(formatQuantity(1000)).toBe("1,000");
    expect(formatQuantity(0)).toBe("0");
    expect(formatQuantity(12.34)).toBe("12.34");
  });

  it("shows up to 6 decimals", () => {
    expect(formatQuantity(0.000001)).toBe("0.000001");
    expect(formatQuantity(1.123456)).toBe("1.123456");
    expect(formatQuantity(1.1234564)).toBe("1.123456");
    expect(formatQuantity(1.1234567)).toBe("1.123457");
    expect(formatQuantity(1e-7)).toBe("0");
  });

  it("removes floating point noise", () => {
    expect(formatQuantity(0.1 + 0.2)).toBe("0.3");
    expect(formatQuantity(1.005 * 3)).toBe("3.015");
  });

  it("handles negative and missing values", () => {
    expect(formatQuantity(-1234.5)).toBe("-1,234.5");
    expect(formatQuantity(-0.0000001)).toBe("0");
    expect(formatQuantity(null)).toBe("-");
    expect(formatQuantity(undefined)).toBe("-");
    expect(formatQuantity(Number.NaN)).toBe("-");
  });

  it("respects minimum and maximum decimals", () => {
    expect(formatQuantity(5, { minDecimals: 2 })).toBe("5.00");
    expect(formatQuantity(5.129, { maxDecimals: 2 })).toBe("5.13");
    expect(formatQuantity(1234.5, { grouping: false })).toBe("1234.5");
  });

  it("round-trips through parseQuantity", () => {
    for (const value of [0.000001, 1.5, 1234.123456, 999999.999999]) {
      expect(parseQuantity(formatQuantity(value))).toMatchObject({ ok: true, value });
    }
  });
});

describe("input and money", () => {
  it("formats a value for an input field without grouping", () => {
    expect(quantityToInput(1234.5)).toBe("1234.5");
    expect(quantityToInput(null)).toBe("");
  });

  it("formats money with 2 decimals", () => {
    expect(formatMoney(1234.5)).toBe("1,234.50");
    expect(formatMoney(0)).toBe("0.00");
    expect(formatMoney(-15000)).toBe("-15,000.00");
    expect(formatMoney(null)).toBe("-");
  });
});
