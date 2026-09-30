import { describe, expect, it } from "vitest";

import { statusTones } from "@/components/feedback/status-badge";
import { documentTypes } from "@/features/approvals/document-types";
import { settingDefinitions } from "@/features/settings/setting-definitions";
import { Permissions } from "@/lib/auth/permissions";
import { navigation } from "@/lib/navigation";

import { locales } from "./config";
import { loadMessages, namespaces } from "./messages";

/** Flattens nested messages to `a.b.c` → text. */
function flatten(node: unknown, prefix = ""): Record<string, string> {
  if (typeof node === "string") {
    return { [prefix]: node };
  }
  const result: Record<string, string> = {};
  for (const [key, value] of Object.entries(node as Record<string, unknown>)) {
    Object.assign(result, flatten(value, prefix ? `${prefix}.${key}` : key));
  }
  return result;
}

/** Names of the `{placeholders}` of a message. */
function placeholders(text: string): string[] {
  return [...text.matchAll(/\{(\w+)/g)].map((match) => match[1] ?? "").sort();
}

describe("messages", async () => {
  const th = flatten(await loadMessages("th"));
  const en = flatten(await loadMessages("en"));

  it("has every namespace in every locale", async () => {
    for (const locale of locales) {
      expect(Object.keys(await loadMessages(locale)).sort()).toEqual([...namespaces].sort());
    }
  });

  it("has the same keys in Thai and English", () => {
    expect(Object.keys(en).sort()).toEqual(Object.keys(th).sort());
  });

  it("has no empty texts", () => {
    for (const [key, text] of [...Object.entries(th), ...Object.entries(en)]) {
      expect(text.trim(), key).not.toBe("");
    }
  });

  it("uses the same placeholders in both languages", () => {
    for (const key of Object.keys(th)) {
      expect(placeholders(en[key] ?? ""), key).toEqual(placeholders(th[key] ?? ""));
    }
  });

  it("translates every status, document type, permission, menu entry and setting", () => {
    const expected = [
      ...Object.keys(statusTones).map((status) => `status.${status}`),
      ...documentTypes.map((type) => `common.documentTypes.${type}`),
      ...Object.values(Permissions).map((code) => `settings.permissions.${code}`),
      ...navigation.flatMap((group) => [`nav.${group.label}`, ...group.items.map((item) => `nav.${item.label}`)]),
      ...settingDefinitions.flatMap((definition) => [
        `settings.general.keys.${definition.key}.label`,
        `settings.general.keys.${definition.key}.description`,
      ]),
    ];
    for (const key of expected) {
      expect(th, key).toHaveProperty([key]);
    }
  });
});
