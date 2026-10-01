/**
 * Public URL of this deployment, used for canonical links, Open Graph, the sitemap and robots.txt.
 * `SITE_URL` wins; on Vercel the production domain is used; locally it falls back to localhost.
 */
export function siteUrl(): string {
  const configured = process.env.SITE_URL?.trim();
  if (configured) {
    return configured.replace(/\/+$/, "");
  }
  const vercel = process.env.VERCEL_PROJECT_PRODUCTION_URL?.trim();
  if (vercel) {
    return `https://${vercel}`;
  }
  return "http://localhost:3000";
}
