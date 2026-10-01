import type { AppLocale } from "./config";

/** Message namespaces; one JSON file per namespace and locale in `src/messages/<locale>/`. */
export const namespaces = [
  "common",
  "validation",
  "errors",
  "status",
  "nav",
  "auth",
  "account",
  "dashboard",
  "masters",
  "approvals",
  "settings",
  "placeholder",
  "landing",
] as const;

/** Name of a message namespace. */
export type Namespace = (typeof namespaces)[number];

/** Loads every namespace of a locale and merges them into one messages object. */
export async function loadMessages(locale: AppLocale): Promise<Record<Namespace, Record<string, unknown>>> {
  const entries = await Promise.all(
    namespaces.map(async (namespace) => {
      const loaded = (await import(`../messages/${locale}/${namespace}.json`)) as { default: Record<string, unknown> };
      return [namespace, loaded.default] as const;
    }),
  );
  return Object.fromEntries(entries) as Record<Namespace, Record<string, unknown>>;
}
