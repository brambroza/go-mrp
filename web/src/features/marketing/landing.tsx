"use client";

import { gsap } from "gsap";
import { ScrollTrigger } from "gsap/ScrollTrigger";
import { ArrowRight, Boxes, Check, Factory, Layers, ShieldCheck, ShoppingCart, Smartphone, Sparkles } from "lucide-react";
import { useTranslations } from "next-intl";
import Link from "next/link";
import { useEffect, useRef, type ReactNode } from "react";

/** Rows of the MRP demo panel: item code, level, need, have, proposed, order-by, kind. */
const MRP_ROWS: ReadonlyArray<readonly [string, string, string, string, string, "make" | "buy" | "enough", boolean]> = [
  ["FG-CREAM-50G", "2,500", "300", "2,500", "18 ต.ค.", "make", false],
  ["SM-BULK-CREAM", "127.5", "20", "107.5", "16 ต.ค.", "make", false],
  ["PK-JAR-50G", "2,525", "1,000", "2,000", "4 ต.ค.", "buy", false],
  ["PK-LABEL", "2,512.5", "0", "5,000", "18 ก.ย.", "buy", true],
  ["RM-OIL", "27.68", "100", "—", "—", "enough", false],
];

/** Lots of the FIFO demo panel: translation key, quantity taken (0 = skipped), bar width in percent. */
const FIFO_LOTS: ReadonlyArray<readonly [string, number, number]> = [
  ["lot1", 70, 100],
  ["lot2", 50, 71],
  ["lot3", 0, 0],
];

/** Figures of the stats band: value, suffix, translation key. */
const STATS: ReadonlyArray<readonly [number, string, "tests" | "decimals" | "rls"]> = [
  [187, "", "tests"],
  [6, "", "decimals"],
  [100, "%", "rls"],
];

const FEATURE_KEYS = ["mrp", "ledger", "lot", "approval", "purchasing", "bom"] as const;
const SOON_KEYS = ["line", "excel", "job", "accounting", "ai"] as const;
const FAQ_KEYS = ["1", "2", "3", "4", "5", "6"] as const;
const PLAN_KEYS = ["starter", "pro", "enterprise"] as const;
const PLAN_USERS: Record<(typeof PLAN_KEYS)[number], number> = { starter: 5, pro: 15, enterprise: 30 };

const FEATURE_ICONS: Record<(typeof FEATURE_KEYS)[number], ReactNode> = {
  mrp: <Factory className="size-5" aria-hidden />,
  ledger: <ShieldCheck className="size-5" aria-hidden />,
  lot: <Boxes className="size-5" aria-hidden />,
  approval: <Smartphone className="size-5" aria-hidden />,
  purchasing: <ShoppingCart className="size-5" aria-hidden />,
  bom: <Layers className="size-5" aria-hidden />,
};

const buttonPrimary =
  "inline-flex items-center justify-center gap-2 rounded-lg bg-primary px-5 py-3 text-sm font-medium text-primary-foreground transition hover:opacity-90 focus-visible:outline-2 focus-visible:outline-ring";
const buttonSecondary =
  "inline-flex items-center justify-center gap-2 rounded-lg border border-border bg-background px-5 py-3 text-sm font-medium transition hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring";

/**
 * Public landing page. Animations run with GSAP + ScrollTrigger and are skipped when the visitor
 * prefers reduced motion; every element is visible without JavaScript, so crawlers read the full page.
 */
export function Landing() {
  const t = useTranslations("landing");
  const root = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!root.current || window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      return;
    }
    gsap.registerPlugin(ScrollTrigger);

    const context = gsap.context(() => {
      gsap.from("[data-hero]", { y: 28, opacity: 0, duration: 0.8, stagger: 0.1, ease: "power3.out" });

      gsap.utils.toArray<HTMLElement>("[data-reveal]").forEach((element) => {
        gsap.from(element, {
          y: 32,
          opacity: 0,
          duration: 0.7,
          ease: "power2.out",
          scrollTrigger: { trigger: element, start: "top 88%", once: true },
        });
      });

      // MRP panel: rows drop in one by one, then the late badge pulses.
      gsap.from("[data-mrp-row]", {
        x: -16,
        opacity: 0,
        duration: 0.45,
        stagger: 0.12,
        ease: "power2.out",
        scrollTrigger: { trigger: "[data-panel='mrp']", start: "top 80%", once: true },
      });
      gsap.fromTo(
        "[data-late]",
        { scale: 0.9 },
        { scale: 1.06, duration: 0.6, repeat: 3, yoyo: true, ease: "power1.inOut", delay: 1, scrollTrigger: { trigger: "[data-panel='mrp']", start: "top 80%", once: true } },
      );

      // FIFO panel: the bars of consumed lots fill from left to right.
      gsap.utils.toArray<HTMLElement>("[data-bar]").forEach((bar, index) => {
        gsap.fromTo(
          bar,
          { width: "0%" },
          {
            width: `${bar.dataset.bar ?? 0}%`,
            duration: 0.9,
            delay: index * 0.35,
            ease: "power2.out",
            scrollTrigger: { trigger: "[data-panel='fifo']", start: "top 80%", once: true },
          },
        );
      });

      // Approval panel: steps tick one after another.
      gsap.from("[data-step]", {
        scale: 0.6,
        opacity: 0,
        duration: 0.5,
        stagger: 0.6,
        ease: "back.out(2)",
        scrollTrigger: { trigger: "[data-panel='approve']", start: "top 80%", once: true },
      });

      // Stats band: numbers count up when they come into view.
      gsap.utils.toArray<HTMLElement>("[data-count]").forEach((element) => {
        const target = Number(element.dataset.count ?? 0);
        const suffix = element.dataset.suffix ?? "";
        const counter = { value: 0 };
        gsap.to(counter, {
          value: target,
          duration: 1.4,
          ease: "power1.out",
          scrollTrigger: { trigger: element, start: "top 90%", once: true },
          onUpdate: () => {
            element.textContent = `${Math.round(counter.value).toLocaleString("en-US")}${suffix}`;
          },
        });
      });
    }, root);

    return () => context.revert();
  }, []);

  return (
    <div ref={root} className="min-h-dvh bg-background text-foreground">
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-md focus:bg-background focus:px-3 focus:py-2">
        {t("nav.skip")}
      </a>

      <header className="sticky top-0 z-40 border-b border-border/60 bg-background/80 backdrop-blur">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-3">
          <Link href="/" className="flex items-center gap-2 font-semibold">
            <span className="grid size-7 place-items-center rounded-md bg-primary text-xs font-bold text-primary-foreground" aria-hidden>
              {"M"}
            </span>
            <span>{t("footer.tagline")}</span>
          </Link>
          <nav aria-label={t("nav.features")} className="hidden items-center gap-6 text-sm text-muted-foreground md:flex">
            <a href="#features" className="hover:text-foreground">{t("nav.features")}</a>
            <a href="#how" className="hover:text-foreground">{t("nav.how")}</a>
            <a href="#pricing" className="hover:text-foreground">{t("nav.pricing")}</a>
            <a href="#faq" className="hover:text-foreground">{t("nav.faq")}</a>
          </nav>
          <div className="flex items-center gap-2">
            <Link href="/login" className="rounded-lg px-3 py-2 text-sm font-medium hover:bg-muted">{t("nav.login")}</Link>
            <Link href="/signup" className={buttonPrimary}>{t("nav.signup")}</Link>
          </div>
        </div>
      </header>

      <main id="main">
        <section className="mx-auto max-w-6xl px-4 pb-16 pt-16 md:pt-24">
          <div className="max-w-3xl">
            <p data-hero className="mb-4 inline-flex items-center gap-2 rounded-full border border-border px-3 py-1 text-xs font-medium text-muted-foreground">
              <Sparkles className="size-3.5" aria-hidden />
              {t("hero.eyebrow")}
            </p>
            <h1 data-hero className="text-balance text-4xl font-semibold leading-tight tracking-tight md:text-6xl">
              {t("hero.title")}
            </h1>
            <p data-hero className="mt-5 max-w-2xl text-pretty text-lg text-muted-foreground">
              {t("hero.subtitle")}
            </p>
            <div data-hero className="mt-8 flex flex-wrap gap-3">
              <Link href="/signup" className={buttonPrimary}>
                {t("hero.primary")}
                <ArrowRight className="size-4" aria-hidden />
              </Link>
              <Link href="/login" className={buttonSecondary}>{t("hero.secondary")}</Link>
            </div>
            <ul data-hero className="mt-8 flex flex-wrap gap-x-6 gap-y-2 text-sm text-muted-foreground">
              {(["trust1", "trust2", "trust3"] as const).map((key) => (
                <li key={key} className="flex items-center gap-2">
                  <Check className="size-4 text-primary" aria-hidden />
                  {t(`hero.${key}`)}
                </li>
              ))}
            </ul>
          </div>
        </section>

        <section aria-labelledby="demo-title" className="border-y border-border bg-muted/40">
          <div className="mx-auto max-w-6xl px-4 py-16">
            <div data-reveal className="mb-10 max-w-2xl">
              <h2 id="demo-title" className="text-balance text-3xl font-semibold tracking-tight">{t("demo.title")}</h2>
              <p className="mt-2 text-muted-foreground">{t("demo.subtitle")}</p>
            </div>

            <div className="grid gap-6 lg:grid-cols-3">
              <article data-reveal data-panel="mrp" className="flex min-w-0 flex-col rounded-xl border border-border bg-card p-5 shadow-sm">
                <PanelHeading label={t("demo.mrp.label")} doc={t("demo.mrp.doc")} sub={t("demo.mrp.need")} />
                <div className="mt-4 overflow-x-auto">
                  <table className="w-full min-w-[420px] text-xs">
                    <thead className="text-muted-foreground">
                      <tr>
                        <th className="pb-2 text-left font-medium">{t("demo.mrp.colItem")}</th>
                        <th className="pb-2 text-right font-medium">{t("demo.mrp.colNeed")}</th>
                        <th className="pb-2 text-right font-medium">{t("demo.mrp.colHave")}</th>
                        <th className="pb-2 text-right font-medium">{t("demo.mrp.colOrder")}</th>
                        <th className="pb-2 text-left font-medium">{t("demo.mrp.colWhen")}</th>
                      </tr>
                    </thead>
                    <tbody className="font-mono tabular-nums">
                      {MRP_ROWS.map(([code, need, have, order, when, kind, late]) => (
                        <tr key={code} data-mrp-row className="border-t border-border/60">
                          <td className="py-2 pr-2">
                            <div className="flex flex-wrap items-center gap-1.5">
                              <span>{code}</span>
                              <Tag tone={kind === "make" ? "info" : kind === "buy" ? "muted" : "ok"}>
                                {kind === "make" ? t("demo.mrp.make") : kind === "buy" ? t("demo.mrp.buy") : t("demo.mrp.enough")}
                              </Tag>
                              {late ? (
                                <span data-late>
                                  <Tag tone="bad">{t("demo.mrp.late")}</Tag>
                                </span>
                              ) : null}
                            </div>
                          </td>
                          <td className="py-2 text-right">{need}</td>
                          <td className="py-2 text-right">{have}</td>
                          <td className="py-2 text-right font-semibold">{order}</td>
                          <td className="py-2 pl-2 text-left">{when}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <p className="mt-4 rounded-lg bg-primary px-3 py-2 text-center text-xs font-medium text-primary-foreground">{t("demo.mrp.cta")}</p>
              </article>

              <article data-reveal data-panel="fifo" className="flex min-w-0 flex-col rounded-xl border border-border bg-card p-5 shadow-sm">
                <PanelHeading label={t("demo.fifo.label")} doc={t("demo.fifo.doc")} />
                <ul className="mt-4 space-y-3">
                  {FIFO_LOTS.map(([key, qty, width]) => (
                    <li key={key} className="rounded-lg border border-border/70 p-3 text-xs">
                      <div className="flex items-center justify-between gap-2">
                        <span className="font-mono">{t(`demo.fifo.${key as "lot1" | "lot2" | "lot3"}`)}</span>
                        <Tag tone={qty > 0 ? "ok" : "warn"}>{qty > 0 ? t("demo.fifo.take", { qty }) : t("demo.fifo.skip")}</Tag>
                      </div>
                      <div className="mt-2 h-2 overflow-hidden rounded-full bg-muted">
                        <div data-bar={width} className="h-full rounded-full bg-primary" style={{ width: `${width}%` }} />
                      </div>
                    </li>
                  ))}
                </ul>
                <p className="mt-4 text-xs text-muted-foreground">{t("demo.fifo.note")}</p>
              </article>

              <article data-reveal data-panel="approve" className="flex min-w-0 flex-col rounded-xl border border-border bg-card p-5 shadow-sm">
                <PanelHeading label={t("demo.approve.label")} doc={t("demo.approve.doc")} />
                <ol className="mt-5 space-y-5">
                  {(["step1", "step2"] as const).map((key, index) => (
                    <li key={key} className="flex items-start gap-3 text-sm">
                      <span
                        data-step
                        className={
                          index === 0
                            ? "grid size-7 shrink-0 place-items-center rounded-full bg-primary text-primary-foreground"
                            : "grid size-7 shrink-0 place-items-center rounded-full border-2 border-dashed border-border text-xs text-muted-foreground"
                        }
                        aria-hidden
                      >
                        {index === 0 ? <Check className="size-4" /> : <span>{index + 1}</span>}
                      </span>
                      <div>
                        <div className="font-medium">{t(`demo.approve.${key}`)}</div>
                        <div className="text-xs text-muted-foreground">{index === 0 ? t("demo.approve.done") : t("demo.approve.waiting")}</div>
                      </div>
                    </li>
                  ))}
                </ol>
                <p className="mt-auto pt-5 text-xs text-muted-foreground">{t("demo.approve.note")}</p>
              </article>
            </div>
          </div>
        </section>

        <section id="features" aria-labelledby="features-title" className="mx-auto max-w-6xl px-4 py-16">
          <div data-reveal className="mb-10 max-w-2xl">
            <h2 id="features-title" className="text-balance text-3xl font-semibold tracking-tight">{t("features.title")}</h2>
            <p className="mt-2 text-muted-foreground">{t("features.subtitle")}</p>
          </div>
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {FEATURE_KEYS.map((key) => (
              <article key={key} data-reveal className="rounded-xl border border-border p-5">
                <div className="mb-3 grid size-9 place-items-center rounded-lg bg-muted text-foreground">{FEATURE_ICONS[key]}</div>
                <h3 className="font-semibold">{t(`features.items.${key}.title`)}</h3>
                <p className="mt-1.5 text-sm text-muted-foreground">{t(`features.items.${key}.body`)}</p>
              </article>
            ))}
          </div>
          <div data-reveal className="mt-10 rounded-xl border border-dashed border-border p-5">
            <div className="flex flex-wrap items-center gap-3">
              <h3 className="font-semibold">{t("features.soonTitle")}</h3>
              <Tag tone="warn">{t("features.soonBadge")}</Tag>
            </div>
            <ul className="mt-3 grid gap-2 text-sm text-muted-foreground sm:grid-cols-2">
              {SOON_KEYS.map((key) => (
                <li key={key} className="flex items-start gap-2">
                  <ArrowRight className="mt-0.5 size-4 shrink-0" aria-hidden />
                  {t(`features.soon.${key}`)}
                </li>
              ))}
            </ul>
          </div>
        </section>

        <section id="how" aria-labelledby="how-title" className="border-y border-border bg-muted/40">
          <div className="mx-auto max-w-6xl px-4 py-16">
            <h2 id="how-title" data-reveal className="text-balance text-3xl font-semibold tracking-tight">{t("how.title")}</h2>
            <ol className="mt-8 grid gap-6 md:grid-cols-3">
              {(["step1", "step2", "step3"] as const).map((key, index) => (
                <li key={key} data-reveal className="rounded-xl border border-border bg-card p-5">
                  <span className="font-mono text-sm text-muted-foreground">{String(index + 1).padStart(2, "0")}</span>
                  <h3 className="mt-2 font-semibold">{t(`how.${key}.title`)}</h3>
                  <p className="mt-1.5 text-sm text-muted-foreground">{t(`how.${key}.body`)}</p>
                </li>
              ))}
            </ol>
          </div>
        </section>

        <section aria-label={t("stats.tests")} className="mx-auto grid max-w-6xl gap-6 px-4 py-16 sm:grid-cols-3">
          {STATS.map(([value, suffix, key]) => (
            <div key={key} data-reveal className="rounded-xl border border-border p-5">
              <div data-count={value} data-suffix={suffix} className="text-4xl font-semibold tabular-nums">
                {`${value.toLocaleString("en-US")}${suffix}`}
              </div>
              <p className="mt-2 text-sm text-muted-foreground">{t(`stats.${key}`)}</p>
            </div>
          ))}
        </section>

        <section id="pricing" aria-labelledby="pricing-title" className="border-y border-border bg-muted/40">
          <div className="mx-auto max-w-6xl px-4 py-16">
            <div data-reveal className="mb-10 max-w-2xl">
              <h2 id="pricing-title" className="text-balance text-3xl font-semibold tracking-tight">{t("pricing.title")}</h2>
              <p className="mt-2 text-muted-foreground">{t("pricing.subtitle")}</p>
            </div>
            <div className="grid gap-5 md:grid-cols-3">
              {PLAN_KEYS.map((key) => (
                <article
                  key={key}
                  data-reveal
                  className={key === "pro" ? "relative rounded-xl border-2 border-primary bg-card p-6" : "rounded-xl border border-border bg-card p-6"}
                >
                  {key === "pro" ? (
                    <span className="absolute -top-3 left-5 rounded-full bg-primary px-3 py-0.5 text-xs font-medium text-primary-foreground">
                      {t("pricing.pro.badge")}
                    </span>
                  ) : null}
                  <h3 className="text-lg font-semibold">{t(`pricing.${key}.name`)}</h3>
                  <p className="mt-3 flex items-baseline gap-1.5">
                    <span className="text-4xl font-semibold tabular-nums">{t(`pricing.${key}.price`)}</span>
                    <span className="text-sm text-muted-foreground">{t("pricing.perMonth")}</span>
                  </p>
                  <p className="mt-1 text-sm text-muted-foreground">{t("pricing.users", { count: PLAN_USERS[key] })}</p>
                  <ul className="mt-5 space-y-2 text-sm">
                    {(["f1", "f2", "f3"] as const).map((feature) => (
                      <li key={feature} className="flex items-start gap-2">
                        <Check className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden />
                        {t(`pricing.${key}.${feature}`)}
                      </li>
                    ))}
                  </ul>
                  <Link href="/signup" className={`${key === "pro" ? buttonPrimary : buttonSecondary} mt-6 w-full`}>
                    {t("pricing.cta")}
                  </Link>
                </article>
              ))}
            </div>
          </div>
        </section>

        <section id="faq" aria-labelledby="faq-title" className="mx-auto max-w-3xl px-4 py-16">
          <h2 id="faq-title" data-reveal className="text-balance text-3xl font-semibold tracking-tight">{t("faq.title")}</h2>
          <div className="mt-8 divide-y divide-border rounded-xl border border-border">
            {FAQ_KEYS.map((key) => (
              <details key={key} data-reveal className="group px-5 py-4">
                <summary className="cursor-pointer list-none font-medium marker:content-none">
                  <span className="flex items-center justify-between gap-4">
                    {t(`faq.q${key}`)}
                    <ArrowRight className="size-4 shrink-0 transition group-open:rotate-90" aria-hidden />
                  </span>
                </summary>
                <p className="mt-3 text-sm text-muted-foreground">{t(`faq.a${key}`)}</p>
              </details>
            ))}
          </div>
        </section>

        <section aria-labelledby="cta-title" className="mx-auto max-w-6xl px-4 pb-20">
          <div data-reveal className="rounded-2xl bg-primary px-6 py-12 text-center text-primary-foreground md:px-12">
            <h2 id="cta-title" className="text-balance text-3xl font-semibold tracking-tight">{t("cta.title")}</h2>
            <p className="mt-3 opacity-80">{t("cta.body")}</p>
            <Link
              href="/signup"
              className="mt-7 inline-flex items-center gap-2 rounded-lg bg-background px-6 py-3 text-sm font-medium text-foreground transition hover:opacity-90"
            >
              {t("cta.button")}
              <ArrowRight className="size-4" aria-hidden />
            </Link>
          </div>
        </section>
      </main>

      <footer className="border-t border-border">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-4 px-4 py-8 text-sm text-muted-foreground">
          <span>{t("footer.tagline")}</span>
          <div className="flex gap-5">
            <Link href="/login" className="hover:text-foreground">{t("footer.login")}</Link>
            <Link href="/signup" className="hover:text-foreground">{t("footer.signup")}</Link>
          </div>
          <span>{`© ${new Date().getFullYear()} · ${t("footer.rights")}`}</span>
        </div>
      </footer>
    </div>
  );
}

/** Heading of a demo panel: label chip, document number and an optional subtitle. */
function PanelHeading({ label, doc, sub }: { label: string; doc: string; sub?: string }) {
  return (
    <div>
      <Tag tone="muted">{label}</Tag>
      <div className="mt-2 font-mono text-sm">{doc}</div>
      {sub ? <div className="text-xs text-muted-foreground">{sub}</div> : null}
    </div>
  );
}

const toneClasses = {
  ok: "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300",
  warn: "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300",
  bad: "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300",
  info: "bg-sky-100 text-sky-800 dark:bg-sky-950 dark:text-sky-300",
  muted: "bg-muted text-muted-foreground",
} as const;

/** Small status pill used inside the demo panels. */
function Tag({ tone, children }: { tone: keyof typeof toneClasses; children: ReactNode }) {
  return <span className={`inline-block rounded-full px-2 py-0.5 text-[11px] font-medium ${toneClasses[tone]}`}>{children}</span>;
}
