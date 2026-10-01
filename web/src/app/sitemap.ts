import type { MetadataRoute } from "next";

import { siteUrl } from "@/lib/site";

/** Only the public landing page is listed; everything else needs a session. */
export default function sitemap(): MetadataRoute.Sitemap {
  const base = siteUrl();
  return [
    {
      url: `${base}/`,
      lastModified: new Date("2026-10-01"),
      changeFrequency: "weekly",
      priority: 1,
      alternates: { languages: { th: `${base}/`, en: `${base}/` } },
    },
  ];
}
