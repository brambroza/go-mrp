import * as Crypto from "expo-crypto";

/** Creates a random UUID (v4) with the platform's secure random generator. */
export function newId(): string {
  return Crypto.randomUUID();
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Returns whether the text is a UUID. */
export function isUuid(text: string | null | undefined): text is string {
  return typeof text === "string" && UUID.test(text);
}
