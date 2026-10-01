import type { MetadataRoute } from "next";

import { siteUrl } from "@/lib/site";

/** Lets crawlers index the landing page only; the app and its API stay out of search results. */
export default function robots(): MetadataRoute.Robots {
  const base = siteUrl();
  return {
    rules: [
      {
        userAgent: "*",
        allow: ["/", "/welcome", "/llms.txt"],
        disallow: ["/api/", "/login", "/signup", "/approvals", "/masters", "/settings", "/account", "/inventory", "/purchasing", "/production"],
      },
    ],
    sitemap: `${base}/sitemap.xml`,
    host: base,
  };
}
