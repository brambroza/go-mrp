/**
 * Returns the path to open after sign-in. Only same-site absolute paths are accepted, so a
 * crafted `?next=https://evil.example` or `?next=//evil.example` cannot redirect off the site.
 */
export function safeNextPath(next: string | null | undefined, fallback = "/"): string {
  if (!next || !next.startsWith("/") || next.startsWith("//") || next.includes("\\") || /[\u0000-\u001f]/.test(next)) {
    return fallback;
  }
  if (next === "/login" || next.startsWith("/login?") || next === "/signup" || next.startsWith("/signup?")) {
    return fallback;
  }
  return next;
}
