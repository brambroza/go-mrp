import {
  formatDisplayDate,
  formatDisplayDateTime,
  isoFromDisplayInput,
  isoFromParts,
  parseIsoDate,
  toIsoDate,
  todayIso,
  zonedDateTimeParts,
} from "./date";

const RealDateTimeFormat = Intl.DateTimeFormat;

/**
 * Simulates a device whose default locale is Thai with the Buddhist calendar and Thai digits:
 * every formatter created without an explicit calendar behaves like `th-TH-u-ca-buddhist-nu-thai`.
 */
function useThaiBuddhistDevice(): void {
  const Patched = function (locales?: Intl.LocalesArgument, options?: Intl.DateTimeFormatOptions) {
    const requested = typeof locales === "string" ? locales : undefined;
    const locale = requested && requested.includes("-u-") ? requested : "th-TH-u-ca-buddhist-nu-thai";
    return new RealDateTimeFormat(locale, options);
  } as unknown as typeof Intl.DateTimeFormat;
  Patched.supportedLocalesOf = RealDateTimeFormat.supportedLocalesOf;
  Intl.DateTimeFormat = Patched;
}

afterEach(() => {
  Intl.DateTimeFormat = RealDateTimeFormat;
  jest.restoreAllMocks();
});

describe("ISO date under a Thai locale", () => {
  it("proves that the simulated device formats the Buddhist year", () => {
    useThaiBuddhistDevice();
    const text = new Intl.DateTimeFormat(undefined, { year: "numeric", timeZone: "Asia/Bangkok" }).format(new Date("2026-09-29T05:00:00Z"));
    expect(text).toContain("๒๕๖๙");
  });

  it("builds a Gregorian date with ASCII digits", () => {
    useThaiBuddhistDevice();
    expect(toIsoDate(new Date("2026-09-29T05:00:00Z"), "Asia/Bangkok")).toBe("2026-09-29");
    expect(todayIso("Asia/Bangkok", new Date("2026-01-01T00:00:00Z"))).toBe("2026-01-01");
  });

  it("is not affected by the Thai locale of toLocaleDateString", () => {
    jest.spyOn(Date.prototype, "toLocaleDateString").mockReturnValue("29/9/2569");
    jest.spyOn(Date.prototype, "toLocaleString").mockReturnValue("29/9/2569 12:00:00");
    expect(toIsoDate(new Date("2026-09-29T05:00:00Z"))).toBe("2026-09-29");
  });

  it("uses the tenant's time zone, not the device's", () => {
    // 18:30 UTC is already the next day in Bangkok (UTC+7).
    expect(toIsoDate(new Date("2026-09-29T18:30:00Z"), "Asia/Bangkok")).toBe("2026-09-30");
    expect(toIsoDate(new Date("2026-09-29T16:59:59Z"), "Asia/Bangkok")).toBe("2026-09-29");
    expect(toIsoDate(new Date("2026-12-31T17:00:00Z"), "Asia/Bangkok")).toBe("2027-01-01");
    expect(toIsoDate(new Date("2026-09-29T18:30:00Z"), "UTC")).toBe("2026-09-29");
  });

  it("falls back to UTC+7 when Intl fails", () => {
    Intl.DateTimeFormat = function () {
      throw new RangeError("unsupported time zone");
    } as unknown as typeof Intl.DateTimeFormat;
    expect(toIsoDate(new Date("2026-09-29T18:30:00Z"))).toBe("2026-09-30");
    expect(zonedDateTimeParts(new Date("2026-02-28T17:05:00Z"))).toEqual({ year: 2026, month: 3, day: 1, hour: 0, minute: 5 });
  });

  it("falls back when the engine ignores the calendar and returns a Buddhist year", () => {
    Intl.DateTimeFormat = function (_locale?: string, options?: Intl.DateTimeFormatOptions) {
      return new RealDateTimeFormat("en-US-u-ca-buddhist-nu-latn", { ...options, calendar: "buddhist" });
    } as unknown as typeof Intl.DateTimeFormat;
    expect(toIsoDate(new Date("2026-09-29T05:00:00Z"))).toBe("2026-09-29");
  });
});

describe("ISO parts", () => {
  it("builds and parses valid dates", () => {
    expect(isoFromParts({ year: 2026, month: 2, day: 28 })).toBe("2026-02-28");
    expect(isoFromParts({ year: 2028, month: 2, day: 29 })).toBe("2028-02-29");
    expect(parseIsoDate("2026-09-29")).toEqual({ year: 2026, month: 9, day: 29 });
  });

  it("rejects dates that do not exist", () => {
    expect(isoFromParts({ year: 2026, month: 2, day: 29 })).toBeNull();
    expect(isoFromParts({ year: 2026, month: 13, day: 1 })).toBeNull();
    expect(isoFromParts({ year: 2026, month: 4, day: 31 })).toBeNull();
    expect(parseIsoDate("2569-09-29")).toBeNull();
    expect(parseIsoDate("29/09/2026")).toBeNull();
    expect(parseIsoDate("")).toBeNull();
    expect(parseIsoDate(null)).toBeNull();
  });
});

describe("display", () => {
  it("shows the Buddhist-era year in Thai and the Gregorian year in English", () => {
    expect(formatDisplayDate("2026-09-29", "th")).toBe("29/09/2569");
    expect(formatDisplayDate("2026-09-29", "en")).toBe("29/09/2026");
    expect(formatDisplayDate(null, "th")).toBe("");
    expect(formatDisplayDate("not a date", "th")).toBe("");
  });

  it("shows timestamps in the tenant's time zone", () => {
    expect(formatDisplayDateTime("2026-09-29T18:30:00Z", "th", "Asia/Bangkok")).toBe("30/09/2569 01:30");
    expect(formatDisplayDateTime("2026-09-29T18:30:00+00:00", "en", "Asia/Bangkok")).toBe("30/09/2026 01:30");
    expect(formatDisplayDateTime("", "th")).toBe("");
    expect(formatDisplayDateTime("garbage", "th")).toBe("");
  });

  it("formats timestamps correctly on a Thai Buddhist device", () => {
    useThaiBuddhistDevice();
    expect(formatDisplayDateTime("2026-09-29T05:07:00Z", "th", "Asia/Bangkok")).toBe("29/09/2569 12:07");
  });
});

describe("date typed by the user", () => {
  it("converts a Buddhist-era year to Gregorian in Thai", () => {
    expect(isoFromDisplayInput("29", "9", "2569", "th")).toBe("2026-09-29");
    expect(isoFromDisplayInput("01", "01", "2570", "th")).toBe("2027-01-01");
  });

  it("keeps the Gregorian year in English", () => {
    expect(isoFromDisplayInput("29", "09", "2026", "en")).toBe("2026-09-29");
  });

  it("rejects impossible input", () => {
    expect(isoFromDisplayInput("31", "02", "2569", "th")).toBeNull();
    expect(isoFromDisplayInput("", "02", "2569", "th")).toBeNull();
    expect(isoFromDisplayInput("1", "2", "26", "en")).toBeNull();
    expect(isoFromDisplayInput("1", "2", "2026", "th")).toBeNull();
    expect(isoFromDisplayInput("a", "2", "2569", "th")).toBeNull();
  });
});
