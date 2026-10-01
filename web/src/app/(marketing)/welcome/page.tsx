import type { Metadata } from "next";
import { headers } from "next/headers";
import { getLocale, getTranslations } from "next-intl/server";

import { Landing } from "@/features/marketing/landing";
import { siteUrl } from "@/lib/site";

const FAQ_KEYS = ["1", "2", "3", "4", "5", "6"] as const;

/**
 * Search metadata of the landing page. It is the only indexable page: the root layout sets
 * `robots: noindex` for the app, and this page overrides it. The page is served at `/` by the
 * proxy for visitors without a session, so the canonical URL is `/`.
 */
export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("landing");
  const locale = await getLocale();
  const base = siteUrl();
  return {
    title: { absolute: t("meta.title") },
    description: t("meta.description"),
    keywords: t("meta.keywords").split(",").map((keyword) => keyword.trim()),
    alternates: { canonical: `${base}/` },
    robots: { index: true, follow: true, googleBot: { index: true, follow: true, "max-snippet": -1, "max-image-preview": "large" } },
    openGraph: {
      type: "website",
      url: `${base}/`,
      siteName: "MRP",
      title: t("meta.title"),
      description: t("meta.description"),
      locale: locale === "th" ? "th_TH" : "en_US",
    },
    twitter: { card: "summary_large_image", title: t("meta.title"), description: t("meta.description") },
  };
}

/** Public landing page with structured data for search engines and AI answer engines. */
export default async function WelcomePage() {
  const t = await getTranslations("landing");
  const nonce = (await headers()).get("x-nonce") ?? undefined;
  const base = siteUrl();

  const structuredData = [
    {
      "@context": "https://schema.org",
      "@type": "SoftwareApplication",
      name: "MRP",
      applicationCategory: "BusinessApplication",
      operatingSystem: "Web, iOS, Android",
      description: t("meta.description"),
      url: `${base}/`,
      inLanguage: ["th", "en"],
      offers: (["starter", "pro", "enterprise"] as const).map((plan) => ({
        "@type": "Offer",
        name: t(`pricing.${plan}.name`),
        price: t(`pricing.${plan}.price`).replace(/,/g, ""),
        priceCurrency: "THB",
        billingIncrement: "P1M",
      })),
      featureList: (["mrp", "ledger", "lot", "approval", "purchasing", "bom"] as const).map((key) => t(`features.items.${key}.title`)),
    },
    {
      "@context": "https://schema.org",
      "@type": "FAQPage",
      mainEntity: FAQ_KEYS.map((key) => ({
        "@type": "Question",
        name: t(`faq.q${key}`),
        acceptedAnswer: { "@type": "Answer", text: t(`faq.a${key}`) },
      })),
    },
    {
      "@context": "https://schema.org",
      "@type": "Organization",
      name: "MRP",
      url: `${base}/`,
      description: t("footer.tagline"),
    },
  ];

  return (
    <>
      <script
        type="application/ld+json"
        nonce={nonce}
        // Structured data is JSON built from translation files, not user input.
        dangerouslySetInnerHTML={{ __html: JSON.stringify(structuredData).replace(/</g, "\\u003c") }}
      />
      <Landing />
    </>
  );
}
