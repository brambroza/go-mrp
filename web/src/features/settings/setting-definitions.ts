/** How a tenant setting is edited. */
export type SettingKind =
  | { type: "boolean" }
  | { type: "percent" }
  | { type: "choice"; options: readonly string[] };

/** A tenant setting the web app can edit. */
export interface SettingDefinition {
  /** Key of the API (`api/src/Mrp.SharedKernel/Domain/SettingKeys.cs`). */
  key: string;
  /** Module the setting belongs to; settings are grouped by it. */
  group: "approval" | "inventory" | "purchasing" | "production";
  /** Editor and value type. */
  kind: SettingKind;
  /** Value the API uses when the tenant has not stored one. */
  defaultValue: boolean | number | string;
}

/**
 * Settings shown on `/settings/general`. Add a line here (and its texts in `settings.json` →
 * `general.keys`) when the API gets a new key.
 */
export const settingDefinitions = [
  { key: "approval.allowSelfApprove", group: "approval", kind: { type: "boolean" }, defaultValue: false },
  { key: "inventory.issueStrategy", group: "inventory", kind: { type: "choice", options: ["Fifo", "Fefo"] }, defaultValue: "Fifo" },
  { key: "inventory.allowNegativeStock", group: "inventory", kind: { type: "boolean" }, defaultValue: false },
  { key: "inventory.qcOnReceive", group: "inventory", kind: { type: "boolean" }, defaultValue: false },
  { key: "purchasing.vatPercent", group: "purchasing", kind: { type: "percent" }, defaultValue: 7 },
  { key: "purchasing.overReceivePercent", group: "purchasing", kind: { type: "percent" }, defaultValue: 0 },
  { key: "production.overIssuePercent", group: "production", kind: { type: "percent" }, defaultValue: 0 },
] as const satisfies readonly SettingDefinition[];

/** Key of an editable setting. */
export type SettingKey = (typeof settingDefinitions)[number]["key"];

/** Name of the form field of a setting; dots would be read as nesting by react-hook-form. */
export function settingFieldName(key: string): string {
  return key.replace(/\./g, "__");
}

/** Values of the settings form keyed by {@link settingFieldName}. */
export type SettingValues = Record<string, boolean | number | string | null>;

/** Whether a stored value has the type the definition expects. */
function matchesKind(kind: SettingKind, value: unknown): value is boolean | number | string {
  switch (kind.type) {
    case "boolean":
      return typeof value === "boolean";
    case "percent":
      return typeof value === "number" && Number.isFinite(value);
    case "choice":
      return typeof value === "string" && kind.options.includes(value);
  }
}

/** Form values from the stored settings; missing or malformed values fall back to the default. */
export function toSettingValues(stored: readonly { key: string; value: unknown }[]): SettingValues {
  const byKey = new Map(stored.map((setting) => [setting.key, setting.value]));
  const values: SettingValues = {};
  for (const definition of settingDefinitions) {
    const value = byKey.get(definition.key);
    values[settingFieldName(definition.key)] = matchesKind(definition.kind, value) ? value : definition.defaultValue;
  }
  return values;
}

/** Request body of `PUT /settings` from the form values. */
export function toSettingsBody(values: SettingValues): { key: string; value: boolean | number | string }[] {
  return settingDefinitions.map((definition) => {
    const value = values[settingFieldName(definition.key)];
    return { key: definition.key, value: matchesKind(definition.kind, value) ? value : definition.defaultValue };
  });
}
