import * as SecureStore from "expo-secure-store";
import { Platform } from "react-native";

/** Minimal key-value storage for secrets. */
export interface SecretStorage {
  /** Reads a value, or `null` when absent. */
  get(key: string): Promise<string | null>;
  /** Writes a value; resolves only after it is persisted. */
  set(key: string, value: string): Promise<void>;
  /** Deletes a value. */
  remove(key: string): Promise<void>;
}

/** Key of the refresh token in the device keychain / keystore. */
export const REFRESH_TOKEN_KEY = "mrp.refresh-token";

/** In-memory storage; used on web (bundling check only) and in tests. Nothing is persisted. */
export function createMemorySecretStorage(): SecretStorage {
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

/**
 * Storage backed by `expo-secure-store` (iOS Keychain / Android Keystore). Values stay on this
 * device only and are readable after the first unlock so that a background sync can refresh.
 */
export function createDeviceSecretStorage(): SecretStorage {
  if (Platform.OS === "web") {
    return createMemorySecretStorage();
  }
  const options: SecureStore.SecureStoreOptions = {
    keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY,
  };
  return {
    get: (key) => SecureStore.getItemAsync(key, options),
    set: (key, value) => SecureStore.setItemAsync(key, value, options),
    remove: (key) => SecureStore.deleteItemAsync(key, options),
  };
}
