import { Platform } from "react-native";

/** Non-secret key-value storage (remembered company code, cached profile, language). */
export interface KeyValueStorage {
  /** Reads a value, or `null` when absent. */
  get(key: string): Promise<string | null>;
  /** Writes a value. */
  set(key: string, value: string): Promise<void>;
  /** Deletes a value. */
  remove(key: string): Promise<void>;
}

/** In-memory storage for tests and web. */
export function createMemoryKeyValueStorage(): KeyValueStorage {
  const values = new Map<string, string>();
  return {
    get: async (key) => values.get(key) ?? null,
    set: async (key, value) => {
      values.set(key, value);
    },
    remove: async (key) => {
      values.delete(key);
    },
  };
}

let deviceStorage: KeyValueStorage | null = null;

/**
 * Storage on the device backed by the SQLite key-value store of `expo-sqlite`. Never put tokens
 * or passwords here; use {@link createDeviceSecretStorage} for secrets.
 */
export function getDeviceKeyValueStorage(): KeyValueStorage {
  if (deviceStorage) {
    return deviceStorage;
  }
  if (Platform.OS === "web") {
    deviceStorage = createMemoryKeyValueStorage();
    return deviceStorage;
  }
  // Loaded lazily so that the web bundle and unit tests never open the native database.
  const load = async () => (await import("expo-sqlite/kv-store")).default;
  deviceStorage = {
    get: async (key) => (await load()).getItem(key),
    set: async (key, value) => {
      await (await load()).setItem(key, value);
    },
    remove: async (key) => {
      await (await load()).removeItem(key);
    },
  };
  return deviceStorage;
}
