import { outstandingInOrderUnit, readQuantity, roundQuantity, textOrNull } from "./lines";

describe("form line helpers", () => {
  it("treats an empty optional quantity as 'not received'", () => {
    expect(readQuantity("", { required: false })).toEqual({ text: "", value: null, error: null });
    expect(readQuantity("", { required: true })).toEqual({ text: "", value: null, error: "empty" });
  });

  it("reports invalid quantities", () => {
    expect(readQuantity("1.1234567", { required: false }).error).toBe("too_many_decimals");
    expect(readQuantity("0", { required: true }).error).toBe("not_positive");
    expect(readQuantity("0", { required: true, allowZero: true })).toEqual({ text: "0", value: 0, error: null });
    expect(readQuantity("12.5", { required: true }).value).toBe(12.5);
  });

  it("converts the outstanding quantity to the unit of the purchase order", () => {
    expect(outstandingInOrderUnit(120, 12)).toBe(10);
    expect(outstandingInOrderUnit(1, 3)).toBe(0.333333);
    expect(outstandingInOrderUnit(2500, 1000)).toBe(2.5);
    expect(outstandingInOrderUnit(5, 0)).toBe(5);
  });

  it("rounds to 6 decimals", () => {
    expect(roundQuantity(0.1 + 0.2)).toBe(0.3);
    expect(roundQuantity(1.0000004)).toBe(1);
  });

  it("maps empty text to null", () => {
    expect(textOrNull("  ")).toBeNull();
    expect(textOrNull(" A1 ")).toBe("A1");
  });
});
