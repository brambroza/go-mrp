import { approveCommentSchema, cleanText, documentSchema, loginSchema, lotNoSchema, rejectCommentSchema, scannedTextSchema, twoFactorCodeSchema } from "./schemas";

const ITEM = "44444444-4444-4444-8444-444444444444";
const WAREHOUSE = "33333333-3333-4333-8333-333333333333";
const document = (overrides: Record<string, unknown> = {}) => ({
  documentType: "Issue",
  documentDate: "2026-09-29",
  warehouseId: WAREHOUSE,
  lines: [{ itemId: ITEM, quantity: 1 }],
  ...overrides,
});
const firstMessage = (result: { success: boolean; error?: { issues: { message: string }[] } }) => result.error?.issues[0]?.message;

describe("login", () => {
  it("trims and accepts valid input", () => {
    expect(loginSchema.parse({ companyCode: " dk-foods ", userName: " somchai ", password: " pass word " })).toEqual({ companyCode: "dk-foods", userName: "somchai", password: " pass word " });
  });

  it("uses the limits of the API", () => {
    expect(firstMessage(loginSchema.safeParse({ companyCode: "ab", userName: "u", password: "p" }))).toBe("validation.company_code_length");
    expect(firstMessage(loginSchema.safeParse({ companyCode: "a".repeat(41), userName: "u", password: "p" }))).toBe("validation.company_code_length");
    expect(firstMessage(loginSchema.safeParse({ companyCode: "abc", userName: "", password: "p" }))).toBe("validation.required");
    expect(firstMessage(loginSchema.safeParse({ companyCode: "abc", userName: "u".repeat(201), password: "p" }))).toBe("validation.too_long");
    expect(firstMessage(loginSchema.safeParse({ companyCode: "abc", userName: "u", password: "p".repeat(101) }))).toBe("validation.too_long");
  });

  it("accepts a TOTP code with spaces", () => {
    expect(twoFactorCodeSchema.parse("123 456")).toBe("123456");
    expect(firstMessage(twoFactorCodeSchema.safeParse("12345"))).toBe("validation.two_factor_format");
    expect(firstMessage(twoFactorCodeSchema.safeParse("abcdef"))).toBe("validation.two_factor_format");
    expect(firstMessage(twoFactorCodeSchema.safeParse(""))).toBe("validation.required");
  });
});

describe("approval remarks", () => {
  it("requires a reason for a rejection", () => {
    expect(firstMessage(rejectCommentSchema.safeParse("   "))).toBe("validation.reject_reason_required");
    expect(rejectCommentSchema.parse(" ราคาสูงเกินงบ ")).toBe("ราคาสูงเกินงบ");
    expect(firstMessage(rejectCommentSchema.safeParse("x".repeat(1001)))).toBe("validation.too_long");
  });

  it("allows an empty remark for an approval", () => {
    expect(approveCommentSchema.parse("")).toBe("");
    expect(firstMessage(approveCommentSchema.safeParse("x".repeat(1001)))).toBe("validation.too_long");
  });
});

describe("scanned text", () => {
  it("removes control characters added by scanners", () => {
    expect(cleanText("\u0002LOT-2609-0001\r\n")).toBe("LOT-2609-0001");
    expect(scannedTextSchema.parse(" LOT-1\t")).toBe("LOT-1");
  });

  it("rejects empty and oversized text", () => {
    expect(scannedTextSchema.safeParse("\n").success).toBe(false);
    expect(scannedTextSchema.safeParse("x".repeat(201)).success).toBe(false);
  });

  it("checks the lot number format of the API", () => {
    expect(lotNoSchema.parse("LOT-2609/0001.A_1".replace("_", "-"))).toBe("LOT-2609/0001.A-1");
    expect(firstMessage(lotNoSchema.safeParse("LOT 1"))).toBe("validation.lot_no_format");
    expect(firstMessage(lotNoSchema.safeParse("ล็อต1"))).toBe("validation.lot_no_format");
    expect(firstMessage(lotNoSchema.safeParse("x".repeat(41)))).toBe("validation.too_long");
  });
});

describe("warehouse document", () => {
  it("accepts a valid document", () => {
    expect(documentSchema.safeParse(document()).success).toBe(true);
  });

  it("requires 1 to 500 lines", () => {
    expect(firstMessage(documentSchema.safeParse(document({ lines: [] })))).toBe("validation.lines_required");
    const lines = Array.from({ length: 501 }, () => ({ itemId: ITEM, quantity: 1 }));
    expect(firstMessage(documentSchema.safeParse(document({ lines })))).toBe("validation.too_many_lines");
  });

  it("requires a positive quantity except for counts", () => {
    expect(firstMessage(documentSchema.safeParse(document({ lines: [{ itemId: ITEM, quantity: 0 }] })))).toBe("validation.quantity_positive");
    expect(firstMessage(documentSchema.safeParse(document({ lines: [{ itemId: ITEM, quantity: -1 }] })))).toBe("validation.quantity_positive");
    expect(documentSchema.safeParse(document({ documentType: "Count", lines: [{ itemId: ITEM, quantity: 0 }] })).success).toBe(true);
    expect(firstMessage(documentSchema.safeParse(document({ documentType: "Count", lines: [{ itemId: ITEM, quantity: -1 }] })))).toBe("validation.quantity_not_negative");
    expect(firstMessage(documentSchema.safeParse(document({ lines: [{ itemId: ITEM, quantity: 1e12 }] })))).toBe("validation.quantity_range");
  });

  it("rejects a Buddhist-era or malformed date", () => {
    expect(firstMessage(documentSchema.safeParse(document({ documentDate: "2569-09-29" })))).toBe("validation.date_invalid");
    expect(firstMessage(documentSchema.safeParse(document({ documentDate: "29/09/2026" })))).toBe("validation.date_invalid");
  });

  it("checks line texts and dates", () => {
    const line = (overrides: Record<string, unknown>) => document({ documentType: "Receipt", lines: [{ itemId: ITEM, quantity: 1, ...overrides }] });
    expect(firstMessage(documentSchema.safeParse(line({ supplierLot: "x".repeat(61) })))).toBe("validation.too_long");
    expect(firstMessage(documentSchema.safeParse(line({ mfgDate: "2026-09-10", expiryDate: "2026-09-09" })))).toBe("validation.expiry_before_mfg");
    expect(documentSchema.safeParse(line({ mfgDate: "2026-09-10", expiryDate: "2026-09-10", supplierLot: null })).success).toBe(true);
    expect(firstMessage(documentSchema.safeParse(line({ itemId: "not-a-uuid" })))).toBe("validation.invalid");
  });

  it("requires a destination for a transfer and leaves room for the queue tag in the remark", () => {
    expect(firstMessage(documentSchema.safeParse(document({ documentType: "Transfer" })))).toBe("validation.to_warehouse_required");
    expect(documentSchema.safeParse(document({ documentType: "Transfer", toWarehouseId: WAREHOUSE })).success).toBe(true);
    expect(documentSchema.safeParse(document({ remark: "x".repeat(455) })).success).toBe(true);
    expect(firstMessage(documentSchema.safeParse(document({ remark: "x".repeat(456) })))).toBe("validation.too_long");
  });
});
