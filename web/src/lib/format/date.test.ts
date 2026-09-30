import { describe, expect, it } from "vitest";

import { formatDate, formatDateTime, isIsoDate, isoToLocalDate, localDateToIso, parseIsoDate, partsToIsoDate, toIsoDate, todayIso } from "./date";

describe("toIsoDate / todayIso", () => {
  it("always produces a Gregorian year, never the Buddhist-era year", () => {
    const instant = new Date("2026-09-29T07:00:00Z");
    expect(toIsoDate(instant)).toBe("2026-09-29");
    expect(toIsoDate(instant)).not.toContain("2569");
  });

  it("uses the calendar date of the time zone, not of the machine", () => {
    // 18:30 UTC is already the next day in Bangkok (UTC+7).
    const lateEvening = new Date("2026-12-31T18:30:00Z");
    expect(toIsoDate(lateEvening, "Asia/Bangkok")).toBe("2027-01-01");
    expect(toIsoDate(lateEvening, "UTC")).toBe("2026-12-31");
    expect(toIsoDate(lateEvening, "America/Los_Angeles")).toBe("2026-12-31");
  });

  it("defaults to Asia/Bangkok", () => {
    expect(todayIso(undefined, new Date("2026-02-28T17:00:00Z"))).toBe("2026-03-01");
  });

  it("pads month, day and year", () => {
    expect(partsToIsoDate({ year: 2026, month: 1, day: 5 })).toBe("2026-01-05");
    expect(partsToIsoDate({ year: 987, month: 12, day: 31 })).toBe("0987-12-31");
  });
});

describe("parseIsoDate", () => {
  it("accepts real calendar dates", () => {
    expect(parseIsoDate("2026-09-29")).toEqual({ year: 2026, month: 9, day: 29 });
    expect(parseIsoDate("2024-02-29")).toEqual({ year: 2024, month: 2, day: 29 });
  });

  it("rejects impossible dates and other formats", () => {
    for (const text of ["2026-02-30", "2025-02-29", "2026-13-01", "2026-00-10", "2026-1-5", "29/09/2026", "2026-09-29T00:00:00Z", "", "abc"]) {
      expect(parseIsoDate(text), text).toBeNull();
    }
    expect(parseIsoDate(null)).toBeNull();
    expect(parseIsoDate(undefined)).toBeNull();
    expect(isIsoDate("2026-09-29")).toBe(true);
    expect(isIsoDate("2569-09-31")).toBe(false);
  });
});

describe("calendar widget conversions", () => {
  it("round-trips through a local Date without shifting the day", () => {
    for (const text of ["2026-01-01", "2026-09-29", "2026-12-31", "2024-02-29"]) {
      expect(localDateToIso(isoToLocalDate(text) as Date)).toBe(text);
    }
  });

  it("returns undefined for invalid text", () => {
    expect(isoToLocalDate("2026-02-30")).toBeUndefined();
    expect(isoToLocalDate("")).toBeUndefined();
  });
});

describe("formatDate", () => {
  it("shows the Buddhist-era year in Thai and the Gregorian year in English", () => {
    expect(formatDate("2026-09-29", { locale: "th" })).toBe("29 ก.ย. 2569");
    expect(formatDate("2026-09-29", { locale: "en" })).toBe("29 Sept 2026".replace("Sept", formatDate("2026-09-01", { locale: "en" }).split(" ")[1] ?? ""));
    expect(formatDate("2026-09-29", { locale: "en" })).toMatch(/^29 Sep\w* 2026$/);
  });

  it("defaults to Thai", () => {
    expect(formatDate("2026-01-05")).toBe("5 ม.ค. 2569");
  });

  it("does not shift date-only values by the time zone", () => {
    expect(formatDate("2026-01-01", { locale: "en", timeZone: "America/Los_Angeles" })).toMatch(/^1 Jan 2026$/);
    expect(formatDate("2026-12-31", { locale: "en", timeZone: "Pacific/Kiritimati" })).toMatch(/^31 Dec 2026$/);
  });

  it("converts timestamps to the time zone first", () => {
    expect(formatDate("2026-12-31T18:30:00Z", { locale: "en" })).toMatch(/^1 Jan 2027$/);
    expect(formatDate("2026-12-31T18:30:00Z", { locale: "en", timeZone: "UTC" })).toMatch(/^31 Dec 2026$/);
    expect(formatDate("2026-12-31T18:30:00Z", { locale: "th" })).toBe("1 ม.ค. 2570");
  });

  it("shows a dash for empty and invalid values", () => {
    expect(formatDate(null)).toBe("–");
    expect(formatDate("")).toBe("–");
    expect(formatDate("not a date")).toBe("–");
    expect(formatDate(undefined, { empty: "" })).toBe("");
  });
});

describe("formatDateTime", () => {
  it("shows the time in Asia/Bangkok with a 24-hour clock", () => {
    expect(formatDateTime("2026-09-29T07:05:00Z", { locale: "th" })).toMatch(/^29 ก\.ย\. 2569 14:05$/);
    expect(formatDateTime("2026-09-29T07:05:00Z", { locale: "en" })).toMatch(/^29 Sep\w* 2026,? 14:05$/);
    expect(formatDateTime("2026-09-29T17:00:00Z", { locale: "en" })).toMatch(/^30 Sep\w* 2026,? 00:00$/);
  });

  it("honours another time zone", () => {
    expect(formatDateTime("2026-09-29T07:05:00Z", { locale: "en", timeZone: "UTC" })).toMatch(/07:05$/);
  });

  it("shows a dash for empty and invalid values", () => {
    expect(formatDateTime(null)).toBe("–");
    expect(formatDateTime("nope")).toBe("–");
  });
});
