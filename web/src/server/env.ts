import "server-only";

/** Base URL of the .NET API as seen from the Next.js server (no trailing slash, no `/api/v1`). */
export function apiBaseUrl(): string {
  const value = process.env.API_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5080";
  return value.replace(/\/+$/, "");
}

/** Whether cookies get the `Secure` attribute; on by default in production. */
export function cookieSecure(): boolean {
  const override = process.env.COOKIE_SECURE;
  if (override === "true" || override === "false") {
    return override === "true";
  }
  return process.env.NODE_ENV === "production";
}

/** Extra origins allowed to call the cookie-backed handlers (comma separated `APP_ORIGIN`). */
export function allowedOrigins(): string[] {
  return (process.env.APP_ORIGIN ?? "")
    .split(",")
    .map((origin) => origin.trim().replace(/\/+$/, "").toLowerCase())
    .filter((origin) => origin.length > 0);
}
