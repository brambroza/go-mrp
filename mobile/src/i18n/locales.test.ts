import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";

import { AppError } from "@/lib/errors";

import { initI18n, resources, translateError } from "./index";

/** Flattens nested translations to dotted keys. */
function flatten(node: unknown, prefix = ""): Record<string, string> {
  if (typeof node === "string") {
    return { [prefix]: node };
  }
  return Object.entries(node as Record<string, unknown>).reduce<Record<string, string>>(
    (all, [key, value]) => ({ ...all, ...flatten(value, prefix ? `${prefix}.${key}` : key) }),
    {},
  );
}

/** All source files of the app. */
function sourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((name) => {
    const path = join(directory, name);
    if (statSync(path).isDirectory()) {
      return name === "node_modules" || name === "locales" ? [] : sourceFiles(path);
    }
    return /\.(ts|tsx)$/.test(name) && !/\.test\.tsx?$/.test(name) ? [path] : [];
  });
}

const th = flatten(resources.th.common);
const en = flatten(resources.en.common);
const root = join(__dirname, "..", "..");
const sources = [...sourceFiles(join(root, "app")), ...sourceFiles(join(root, "src"))].map((path) => readFileSync(path, "utf8"));

describe("translations", () => {
  it("has the same keys in Thai and English", () => {
    expect(Object.keys(en).sort()).toEqual(Object.keys(th).sort());
    expect(Object.keys(resources.en.errors).sort()).toEqual(Object.keys(resources.th.errors).sort());
  });

  it("has no empty text and keeps the placeholders of both languages equal", () => {
    const placeholders = (text: string) => (text.match(/\{\{\w+\}\}/g) ?? []).sort();
    for (const [key, text] of Object.entries(th)) {
      expect(text.trim()).not.toBe("");
      expect(en[key]?.trim()).not.toBe("");
      expect({ key, placeholders: placeholders(en[key] ?? "") }).toEqual({ key, placeholders: placeholders(text) });
    }
  });

  it("defines every key used in the code", () => {
    const used = new Set<string>();
    const dynamic = new Set<string>();
    const pattern = /(?:\bt\(\s*|titleKey:\s*|["'`])((?:validation|common|offline|permission|login|home|settings|approvals|documentType|poStatus|qc|warehouse|location|item|lot|scanner|date|document|submit|receive|issue|transfer|count|queue)\.[A-Za-z0-9_.]+)(\$\{)?/g;
    for (const source of sources) {
      for (const match of source.matchAll(pattern)) {
        const key = match[1] ?? "";
        (match[2] ? dynamic : used).add(key.replace(/\.$/, ""));
      }
    }
    expect(used.size).toBeGreaterThan(200);
    const errorCodes: Record<string, string> = resources.th.errors;
    const missing = [...used].filter((key) => !(key in th) && !(key in errorCodes) && !Object.keys(th).some((known) => known.startsWith(`${key}.`)));
    expect(missing).toEqual([]);
    const missingGroups = [...dynamic].filter((prefix) => !Object.keys(th).some((known) => known.startsWith(`${prefix}.`)));
    expect(missingGroups).toEqual([]);
  });

  it("translates the statuses and kinds that are built at run time", () => {
    const required = [
      ...["Pending", "Approved", "Rejected", "Withdrawn"].map((status) => `approvals.status.${status}`),
      ...["Approve", "Reject", "Withdraw"].map((action) => `approvals.action.${action}`),
      ...["Quarantine", "Released", "Rejected", "OnHold"].map((status) => `qc.${status}`),
      ...["pending", "sending", "failed", "sent", "approval"].map((status) => `queue.status.${status}`),
      ...["sent", "approval", "queued", "failed"].map((status) => `queue.resultStatus.${status}`),
      ...["receipt", "issue", "transfer", "count"].map((kind) => `queue.kind.${kind}`),
      ...["empty", "invalid", "too_many_decimals", "out_of_range", "not_positive", "precision"].map((code) => `validation.quantity.${code}`),
      ...["missing", "invalid", "insecure"].map((code) => `login.config.${code}`),
      ...["system", "light", "dark"].map((theme) => `settings.themes.${theme}`),
    ];
    expect(required.filter((key) => !(key in th))).toEqual([]);
  });

  it("does not hard-code Thai text in screens and components", () => {
    const offenders = sources.filter((source) => /[฀-๿]/.test(source.replace(/\/\*[\s\S]*?\*\/|\/\/.*$/gm, "").replace(/(label|accessibilityLabel)="(ไทย|ภาษาไทย)"/g, "").replace(/\/\[๐-๙\]\/g|"๐"/g, "")));
    expect(offenders).toEqual([]);
  });
});

describe("error translation", () => {
  const i18n = initI18n();
  const t = i18n.getFixedT("th");

  it("translates by code", () => {
    expect(translateError(t, new AppError({ kind: "http", status: 422, code: "inventory.insufficient_stock", detail: "Not enough stock." }))).toBe("ยอดคงเหลือไม่พอ");
  });

  it("falls back to the detail of the API for an unknown code", () => {
    expect(translateError(t, new AppError({ kind: "http", status: 400, code: "production.unknown_rule", detail: "Line 2: something new." }))).toBe("Line 2: something new.");
  });

  it("uses a general Thai message when nothing else is known", () => {
    expect(translateError(t, new AppError({ kind: "network" }))).toContain("เชื่อมต่อเซิร์ฟเวอร์ไม่ได้");
    expect(translateError(t, new AppError({ kind: "http", status: 503 }))).toContain("ปิดปรับปรุง");
    expect(translateError(t, {})).toBe("เกิดข้อผิดพลาด กรุณาลองใหม่");
  });

  it("translates the error stored with a queue item and validation keys", () => {
    expect(translateError(t, { code: "inventory.receipt.over_receive", detail: "x", status: 400 })).toBe("รับเกินจำนวนที่สั่งซื้อ");
    expect(translateError(t, { code: "validation.lines_required" })).toBe("ต้องมีอย่างน้อย 1 รายการ");
  });

  it("translates to English", () => {
    expect(translateError(i18n.getFixedT("en"), { code: "inventory.insufficient_stock" })).toBe("Not enough stock.");
  });
});
