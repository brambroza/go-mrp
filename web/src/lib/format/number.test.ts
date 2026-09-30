import { describe, expect, it } from "vitest";

import { formatMoney, formatPercent, formatQuantity, parseDecimal, roundTo, toInputText } from "./number";

describe("roundTo", () => {
  it("rounds half away from zero like the API", () => {
    expect(roundTo(1.005, 2)).toBe(1.01);
    expect(roundTo(-1.005, 2)).toBe(-1.01);
    expect(roundTo(2.5, 0)).toBe(3);
    expect(roundTo(-2.5, 0)).toBe(-3);
    expect(roundTo(0.1 + 0.2, 6)).toBe(0.3);
  });

  it("keeps values that already fit", () => {
    expect(roundTo(12.3456, 4)).toBe(12.3456);
    expect(roundTo(0, 4)).toBe(0);
    expect(Object.is(roundTo(-0.00001, 2), 0)).toBe(true);
  });

  it("handles magnitudes printed in exponent form", () => {
    expect(roundTo(1e-7, 6)).toBe(0);
    expect(roundTo(1.5e-6, 6)).toBe(0.000002);
    expect(roundTo(1e21, 2)).toBe(1e21);
  });

  it("passes non-finite values through", () => {
    expect(roundTo(Number.NaN, 2)).toBeNaN();
    expect(roundTo(Number.POSITIVE_INFINITY, 2)).toBe(Number.POSITIVE_INFINITY);
  });
});

describe("formatMoney", () => {
  it("shows two decimals and thousands separators", () => {
    expect(formatMoney(1234.5)).toBe("1,234.50");
    expect(formatMoney(0)).toBe("0.00");
    expect(formatMoney(-9876543.21)).toBe("-9,876,543.21");
  });

  it("keeps up to four decimals, the storage precision", () => {
    expect(formatMoney(12.3456)).toBe("12.3456");
    expect(formatMoney(12.34567)).toBe("12.3457");
    expect(formatMoney(12.3)).toBe("12.30");
  });

  it("shows the baht sign for THB and a suffix for other currencies", () => {
    expect(formatMoney(100, { currency: "THB" })).toBe("฿100.00");
    expect(formatMoney(100, { currency: "thb" })).toBe("฿100.00");
    expect(formatMoney(100, { currency: "USD" })).toBe("100.00 USD");
  });

  it("uses Latin digits in both locales", () => {
    expect(formatMoney(1234.5, { locale: "th" })).toBe("1,234.50");
    expect(formatMoney(1234.5, { locale: "en" })).toBe("1,234.50");
  });

  it("shows a dash for missing values", () => {
    expect(formatMoney(null)).toBe("–");
    expect(formatMoney(undefined)).toBe("–");
    expect(formatMoney(Number.NaN)).toBe("–");
    expect(formatMoney(null, { empty: "" })).toBe("");
  });
});

describe("formatQuantity", () => {
  it("drops trailing zeros and keeps up to six decimals", () => {
    expect(formatQuantity(1250)).toBe("1,250");
    expect(formatQuantity(1250.5)).toBe("1,250.5");
    expect(formatQuantity(0.000001)).toBe("0.000001");
    expect(formatQuantity(0.0000004)).toBe("0");
    expect(formatQuantity(1.1234567)).toBe("1.123457");
  });

  it("appends the unit", () => {
    expect(formatQuantity(12, { unit: "KG" })).toBe("12 KG");
  });

  it("shows a dash for missing values", () => {
    expect(formatQuantity(null)).toBe("–");
  });
});

describe("formatPercent", () => {
  it("formats values that are already in percent", () => {
    expect(formatPercent(7)).toBe("7%");
    expect(formatPercent(7.255)).toBe("7.26%");
    expect(formatPercent(null, { empty: "default" })).toBe("default");
  });
});

describe("parseDecimal", () => {
  it("parses plain and grouped numbers", () => {
    expect(parseDecimal("1,234.5", 4)).toBe(1234.5);
    expect(parseDecimal(" 12 ", 2)).toBe(12);
    expect(parseDecimal("฿1,000", 2)).toBe(1000);
    expect(parseDecimal(".5", 2)).toBe(0.5);
    expect(parseDecimal("5.", 2)).toBe(5);
    expect(parseDecimal("-3.2", 2)).toBe(-3.2);
  });

  it("rounds to the allowed decimals", () => {
    expect(parseDecimal("1.23456", 4)).toBe(1.2346);
    expect(parseDecimal("1.9", 0)).toBe(2);
  });

  it("returns null for empty input and undefined for text that is not a number", () => {
    expect(parseDecimal("", 2)).toBeNull();
    expect(parseDecimal("   ", 2)).toBeNull();
    expect(parseDecimal("abc", 2)).toBeUndefined();
    expect(parseDecimal("1.2.3", 2)).toBeUndefined();
    expect(parseDecimal("1e5", 2)).toBeUndefined();
    expect(parseDecimal("-", 2)).toBeUndefined();
    expect(parseDecimal("๑๒", 2)).toBeUndefined();
  });
});

describe("toInputText", () => {
  it("prints numbers without grouping for editing", () => {
    expect(toInputText(1234.5, 4)).toBe("1234.5");
    expect(toInputText(1000000, 0)).toBe("1000000");
    expect(toInputText(null, 2)).toBe("");
  });
});
