import { beginScan, completeScan, isScanPending } from "./scanBus";

describe("scanner result", () => {
  it("returns the scanned text to the caller", async () => {
    const waiting = beginScan();
    expect(isScanPending()).toBe(true);
    completeScan("LOT-2609-0001");
    expect(await waiting).toBe("LOT-2609-0001");
    expect(isScanPending()).toBe(false);
  });

  it("returns null when the scanner is closed", async () => {
    const waiting = beginScan();
    completeScan(null);
    expect(await waiting).toBeNull();
  });

  it("delivers a result only once", async () => {
    const waiting = beginScan();
    completeScan("A");
    completeScan("B");
    expect(await waiting).toBe("A");
  });

  it("cancels the previous request when a new scan starts", async () => {
    const first = beginScan();
    const second = beginScan();
    expect(await first).toBeNull();
    completeScan("C");
    expect(await second).toBe("C");
  });
});
