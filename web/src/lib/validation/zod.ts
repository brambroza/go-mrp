import { z } from "zod";

/**
 * Validation messages are i18n keys, not text. A message is encoded as `@key` or
 * `@key|{"max":40}` and decoded by the form field components, which translate it with the
 * `validation` namespace. Messages coming from the API are plain text and shown as they are.
 */
const PREFIX = "@";

/** Keys of the `validation` message namespace. */
export type ValidationKey =
  | "required"
  | "invalid"
  | "tooShort"
  | "tooLong"
  | "tooSmall"
  | "tooBig"
  | "tooFew"
  | "tooMany"
  | "email"
  | "pattern"
  | "integer"
  | "date"
  | "companyCode"
  | "userName"
  | "password"
  | "passwordMismatch"
  | "code"
  | "taxId"
  | "currency"
  | "prefix"
  | "totp"
  | "dateOrder"
  | "maxBelowMin"
  | "sameUnit"
  | "reasonRequired";

/** Values interpolated into a validation message. */
export type ValidationValues = Record<string, string | number>;

/** Encodes an i18n validation message for use as a zod error message. */
export function msg(key: ValidationKey, values?: ValidationValues): string {
  return values ? `${PREFIX}${key}|${JSON.stringify(values)}` : `${PREFIX}${key}`;
}

/** Decodes a message created by {@link msg}; returns `null` for plain text. */
export function decodeMessage(message: string | undefined): { key: ValidationKey; values?: ValidationValues } | null {
  if (!message?.startsWith(PREFIX)) {
    return null;
  }
  const separator = message.indexOf("|");
  if (separator < 0) {
    return { key: message.slice(1) as ValidationKey };
  }
  try {
    return {
      key: message.slice(1, separator) as ValidationKey,
      values: JSON.parse(message.slice(separator + 1)) as ValidationValues,
    };
  } catch {
    return { key: message.slice(1, separator) as ValidationKey };
  }
}

z.config({
  customError: (issue) => {
    switch (issue.code) {
      case "invalid_type":
        return issue.input === undefined || issue.input === null || issue.input === "" ? msg("required") : msg("invalid");
      case "too_small": {
        const minimum = Number(issue.minimum);
        if (issue.origin === "string") {
          return minimum <= 1 ? msg("required") : msg("tooShort", { min: minimum });
        }
        if (issue.origin === "array" || issue.origin === "set") {
          return minimum <= 1 ? msg("required") : msg("tooFew", { min: minimum });
        }
        return msg("tooSmall", { min: minimum });
      }
      case "too_big": {
        const maximum = Number(issue.maximum);
        if (issue.origin === "string") {
          return msg("tooLong", { max: maximum });
        }
        if (issue.origin === "array" || issue.origin === "set") {
          return msg("tooMany", { max: maximum });
        }
        return msg("tooBig", { max: maximum });
      }
      case "invalid_format":
        return issue.format === "email" ? msg("email") : msg("pattern");
      case "not_multiple_of":
        return msg("integer");
      default:
        return msg("invalid");
    }
  },
});

export { z };

/** Required text, trimmed, between `min` and `max` characters. */
export function requiredText(max: number, min = 1) {
  return z.string().trim().min(min).max(max);
}

/** Optional text, trimmed, at most `max` characters; empty means "not set". */
export function optionalText(max: number) {
  return z.string().trim().max(max);
}

/** Optional e-mail address, at most `max` characters. */
export function optionalEmail(max = 200) {
  return z
    .string()
    .trim()
    .max(max)
    .refine((value) => value === "" || z.email().safeParse(value).success, { error: msg("email") });
}

/** Required e-mail address. */
export function requiredEmail(max = 200) {
  return z.string().trim().min(1).max(max).pipe(z.email());
}

/** Required number in a range. */
export function requiredNumber(min: number, max: number) {
  return z.number({ error: msg("required") }).min(min).max(max);
}

/** Optional number in a range; `null` means "not set". */
export function optionalNumber(min: number, max: number) {
  return z.number().min(min).max(max).nullable();
}

/** Required whole number in a range. */
export function requiredInteger(min: number, max: number) {
  return z.number({ error: msg("required") }).int().min(min).max(max);
}

/** Optional whole number in a range; `null` means "not set". */
export function optionalInteger(min: number, max: number) {
  return z.number().int().min(min).max(max).nullable();
}

/** Required identifier of a referenced record (uuid). */
export function requiredId() {
  return z.string().min(1).pipe(z.uuid());
}

/** Optional identifier of a referenced record; empty text means "not set". */
export function optionalId() {
  return z.string().refine((value) => value === "" || z.uuid().safeParse(value).success, { error: msg("invalid") });
}

/** Required `YYYY-MM-DD` date. */
export function requiredDate() {
  return z.string().min(1).pipe(z.iso.date({ error: msg("date") }));
}

/** Optional `YYYY-MM-DD` date; empty text means "not set". */
export function optionalDate() {
  return z.string().refine((value) => value === "" || z.iso.date().safeParse(value).success, { error: msg("date") });
}

/** Password rule of the API (ASP.NET Identity options): 8-100 characters with a lowercase letter and a digit. */
export function password() {
  return z
    .string()
    .min(8)
    .max(100)
    .refine((value) => /[a-z]/.test(value) && /\d/.test(value), { error: msg("password") });
}

/** Converts empty or whitespace-only text to `null` for the API. */
export function emptyToNull(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? "";
  return trimmed === "" ? null : trimmed;
}
