"use client";

import { gsap } from "gsap";
import { ScrollTrigger } from "gsap/ScrollTrigger";
import { ArrowRight, Boxes, Check, Factory, Layers, ShieldCheck, ShoppingCart, Smartphone, Sparkles } from "lucide-react";
import { useTranslations } from "next-intl";
import Link from "next/link";
import { useEffect, useRef, type ReactNode } from "react";

/*
 * Visual direction of the landing page (independent of the app theme, always light):
 * white ground with a faint technical grid, ink-navy for weight and trust, one azure accent for
 * "new technology", thin slate borders, generous spacing. Everything else stays quiet.
 */
const ink = "text-slate-900";
const soft = "text-slate-600";
const line = "border-slate-200";
const panel = `rounded-2xl border ${line} bg-white shadow-[0_1px_2px_rgba(15,23,42,0.04),0_12px_40px_-24px_rgba(15,23,42,0.25)]`;
const buttonPrimary =
  "inline-flex items-center justify-center gap-2 rounded-xl bg-slate-950 px-5 py-3 text-sm font-medium text-white shadow-[0_8px_24px_-12px_rgba(2,6,23,0.6)] transition hover:bg-slate-800 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sky-500";
const buttonSecondary =
  "inline-flex items-center justify-center gap-2 rounded-xl border border-slate-300 bg-white px-5 py-3 text-sm font-medium text-slate-900 transition hover:border-slate-400 hover:bg-slate-50 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-sky-500";

/** Rows of the MRP demo panel: item code, need, have, proposed, order-by, kind, late. */
const MRP_ROWS: ReadonlyArray<readonly [string, string, string, string, string, "make" | "buy" | "enough", boolean]> = [
  ["FG-CREAM-50G", "2,500", "300", "2,500", "18 ต.ค.", "make", false],
  ["SM-BULK-CREAM", "127.5", "20", "107.5", "16 ต.ค.", "make", false],
  ["PK-JAR-50G", "2,525", "1,000", "2,000", "4 ต.ค.", "buy", false],
  ["PK-LABEL", "2,512.5", "0", "5,000", "18 ก.ย.", "buy", true],
  ["RM-OIL", "27.68", "100", "—", "—", "enough", false],
];

/** Lots of the FIFO demo panel: translation key, quantity taken (0 = skipped), bar width in percent. */
const FIFO_LOTS: ReadonlyArray<readonly ["lot1" | "lot2" | "lot3", number, number]> = [
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
    <div ref={root} className={`min-h-dvh bg-white ${ink} antialiased`} style={{ colorScheme: "light" }}>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-md focus:bg-white focus:px-3 focus:py-2 focus:shadow"
      >
        {t("nav.skip")}
      </a>

      <header className={`sticky top-0 z-40 border-b ${line} bg-white/80 backdrop-blur-md`}>
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-3">
          <Link href="/" className="flex items-center gap-2.5 font-semibold">
            <span className="grid size-7 place-items-center rounded-lg bg-slate-950 text-xs font-bold text-white" aria-hidden>
              {"M"}
            </span>
            <span className="tracking-tight">{t("footer.tagline")}</span>
          </Link>
          <nav aria-label={t("nav.features")} className={`hidden items-center gap-7 text-sm ${soft} md:flex`}>
            <a href="#features" className="transition hover:text-slate-900">{t("nav.features")}</a>
            <a href="#how" className="transition hover:text-slate-900">{t("nav.how")}</a>
            <a href="#pricing" className="transition hover:text-slate-900">{t("nav.pricing")}</a>
            <a href="#faq" className="transition hover:text-slate-900">{t("nav.faq")}</a>
          </nav>
          <div className="flex items-center gap-2">
            <Link href="/login" className="rounded-xl px-3 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-100">{t("nav.login")}</Link>
            <Link href="/signup" className={buttonPrimary}>{t("nav.signup")}</Link>
          </div>
        </div>
      </header>

      <main id="main">
        <section className="relative overflow-hidden">
          <div
            aria-hidden
            className="pointer-events-none absolute inset-0 bg-[linear-gradient(to_right,rgba(15,23,42,0.045)_1px,transparent_1px),linear-gradient(to_bottom,rgba(15,23,42,0.045)_1px,transparent_1px)] bg-[size:48px_48px] [mask-image:radial-gradient(ellipse_at_top,black_30%,transparent_75%)]"
          />
          <div aria-hidden className="pointer-events-none absolute -top-40 left-1/2 h-[520px] w-[900px] -translate-x-1/2 rounded-full bg-sky-200/50 blur-3xl" />
          <div className="relative mx-auto max-w-6xl px-4 pb-20 pt-20 md:pt-28">
            <div className="max-w-3xl">
              <p data-hero className={`mb-5 inline-flex items-center gap-2 rounded-full border ${line} bg-white/70 px-3 py-1 text-xs font-medium ${soft}`}>
                <Sparkles className="size-3.5 text-sky-600" aria-hidden />
                {t("hero.eyebrow")}
              </p>
              <h1 data-hero className="text-balance text-4xl font-semibold leading-[1.1] tracking-tight text-slate-950 md:text-6xl">
                {t("hero.title")}
              </h1>
              <p data-hero className={`mt-6 max-w-2xl text-pretty text-lg leading-relaxed ${soft}`}>
                {t("hero.subtitle")}
              </p>
              <div data-hero className="mt-9 flex flex-wrap gap-3">
                <Link href="/signup" className={buttonPrimary}>
                  {t("hero.primary")}
                  <ArrowRight className="size-4" aria-hidden />
                </Link>
                <Link href="/login" className={buttonSecondary}>{t("hero.secondary")}</Link>
              </div>
              <ul data-hero className={`mt-9 flex flex-wrap gap-x-6 gap-y-2 text-sm ${soft}`}>
                {(["trust1", "trust2", "trust3"] as const).map((key) => (
                  <li key={key} className="flex items-center gap-2">
                    <span className="grid size-4 place-items-center rounded-full bg-sky-100 text-sky-700" aria-hidden>
                      <Check className="size-3" />
                    </span>
                    {t(`hero.${key}`)}
                  </li>
                ))}
              </ul>
            </div>
          </div>
        </section>

        <section aria-labelledby="demo-title" className={`border-y ${line} bg-slate-50/70`}>
          <div className="mx-auto max-w-6xl px-4 py-20">
            <div data-reveal className="mb-10 max-w-2xl">
              <h2 id="demo-title" className="text-balance text-3xl font-semibold tracking-tight text-slate-950">{t("demo.title")}</h2>
              <p className={`mt-2 ${soft}`}>{t("demo.subtitle")}</p>
            </div>

            <div className="grid gap-6 lg:grid-cols-3">
              <article data-reveal data-panel="mrp" className={`flex min-w-0 flex-col p-5 ${panel}`}>
                <PanelHeading label={t("demo.mrp.label")} doc={t("demo.mrp.doc")} sub={t("demo.mrp.need")} />
                <div className="mt-4 overflow-x-auto">
                  <table className="w-full min-w-[420px] text-xs">
                    <thead className={soft}>
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
                        <tr key={code} data-mrp-row className="border-t border-slate-100">
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
                          <td className="py-2 text-right font-semibold text-slate-950">{order}</td>
                          <td className="py-2 pl-2 text-left">{when}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <p className="mt-4 rounded-xl bg-slate-950 px-3 py-2 text-center text-xs font-medium text-white">{t("demo.mrp.cta")}</p>
              </article>

              <article data-reveal data-panel="fifo" className={`flex min-w-0 flex-col p-5 ${panel}`}>
                <PanelHeading label={t("demo.fifo.label")} doc={t("demo.fifo.doc")} />
                <ul className="mt-4 space-y-3">
                  {FIFO_LOTS.map(([key, qty, width]) => (
                    <li key={key} className={`rounded-xl border ${line} p-3 text-xs`}>
                      <div className="flex items-center justify-between gap-2">
                        <span className="font-mono">{t(`demo.fifo.${key}`)}</span>
                        <Tag tone={qty > 0 ? "ok" : "warn"}>{qty > 0 ? t("demo.fifo.take", { qty }) : t("demo.fifo.skip")}</Tag>
                      </div>
                      <div className="mt-2 h-2 overflow-hidden rounded-full bg-slate-100">
                        <div data-bar={width} className="h-full rounded-full bg-gradient-to-r from-sky-500 to-sky-400" style={{ width: `${width}%` }} />
                      </div>
                    </li>
                  ))}
                </ul>
                <p className={`mt-4 text-xs ${soft}`}>{t("demo.fifo.note")}</p>
              </article>

              <article data-reveal data-panel="approve" className={`flex min-w-0 flex-col p-5 ${panel}`}>
                <PanelHeading label={t("demo.approve.label")} doc={t("demo.approve.doc")} />
                <ol className="mt-5 space-y-5">
                  {(["step1", "step2"] as const).map((key, index) => (
                    <li key={key} className="flex items-start gap-3 text-sm">
                      <span
                        data-step
                        className={
                          index === 0
                            ? "grid size-7 shrink-0 place-items-center rounded-full bg-sky-600 text-white"
                            : "grid size-7 shrink-0 place-items-center rounded-full border-2 border-dashed border-slate-300 text-xs text-slate-500"
                        }
                        aria-hidden
                      >
                        {index === 0 ? <Check className="size-4" /> : <span>{index + 1}</span>}
                      </span>
                      <div>
                        <div className="font-medium">{t(`demo.approve.${key}`)}</div>
                        <div className={`text-xs ${soft}`}>{index === 0 ? t("demo.approve.done") : t("demo.approve.waiting")}</div>
                      </div>
                    </li>
                  ))}
                </ol>
                <p className={`mt-auto pt-5 text-xs ${soft}`}>{t("demo.approve.note")}</p>
              </article>
            </div>
          </div>
        </section>

        <section id="features" aria-labelledby="features-title" className="mx-auto max-w-6xl px-4 py-20">
          <div data-reveal className="mb-10 max-w-2xl">
            <h2 id="features-title" className="text-balance text-3xl font-semibold tracking-tight text-slate-950">{t("features.title")}</h2>
            <p className={`mt-2 ${soft}`}>{t("features.subtitle")}</p>
          </div>
          <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
            {FEATURE_KEYS.map((key) => (
              <article key={key} data-reveal className={`group rounded-2xl border ${line} bg-white p-6 transition hover:border-slate-300 hover:shadow-[0_12px_40px_-24px_rgba(15,23,42,0.3)]`}>
                <div className="mb-4 grid size-10 place-items-center rounded-xl bg-slate-950 text-white">{FEATURE_ICONS[key]}</div>
                <h3 className="font-semibold text-slate-950">{t(`features.items.${key}.title`)}</h3>
                <p className={`mt-2 text-sm leading-relaxed ${soft}`}>{t(`features.items.${key}.body`)}</p>
              </article>
            ))}
          </div>
          <div data-reveal className="mt-10 rounded-2xl border border-dashed border-slate-300 bg-slate-50/60 p-6">
            <div className="flex flex-wrap items-center gap-3">
              <h3 className="font-semibold text-slate-950">{t("features.soonTitle")}</h3>
              <Tag tone="warn">{t("features.soonBadge")}</Tag>
            </div>
            <ul className={`mt-3 grid gap-2 text-sm ${soft} sm:grid-cols-2`}>
              {SOON_KEYS.map((key) => (
                <li key={key} className="flex items-start gap-2">
                  <ArrowRight className="mt-0.5 size-4 shrink-0 text-sky-600" aria-hidden />
                  {t(`features.soon.${key}`)}
                </li>
              ))}
            </ul>
          </div>
        </section>

        <section id="how" aria-labelledby="how-title" className={`border-y ${line} bg-slate-50/70`}>
          <div className="mx-auto max-w-6xl px-4 py-20">
            <h2 id="how-title" data-reveal className="text-balance text-3xl font-semibold tracking-tight text-slate-950">{t("how.title")}</h2>
            <ol className="mt-8 grid gap-6 md:grid-cols-3">
              {(["step1", "step2", "step3"] as const).map((key, index) => (
                <li key={key} data-reveal className={`p-6 ${panel}`}>
                  <span className="font-mono text-sm text-sky-600">{String(index + 1).padStart(2, "0")}</span>
                  <h3 className="mt-2 font-semibold text-slate-950">{t(`how.${key}.title`)}</h3>
                  <p className={`mt-2 text-sm leading-relaxed ${soft}`}>{t(`how.${key}.body`)}</p>
                </li>
              ))}
            </ol>
          </div>
        </section>

        <section aria-label={t("stats.tests")} className="mx-auto grid max-w-6xl gap-6 px-4 py-20 sm:grid-cols-3">
          {STATS.map(([value, suffix, key]) => (
            <div key={key} data-reveal className={`rounded-2xl border ${line} p-6`}>
              <div data-count={value} data-suffix={suffix} className="text-4xl font-semibold tabular-nums tracking-tight text-slate-950">
                {`${value.toLocaleString("en-US")}${suffix}`}
              </div>
              <p className={`mt-2 text-sm ${soft}`}>{t(`stats.${key}`)}</p>
            </div>
          ))}
        </section>

        <section id="pricing" aria-labelledby="pricing-title" className={`border-y ${line} bg-slate-50/70`}>
          <div className="mx-auto max-w-6xl px-4 py-20">
            <div data-reveal className="mb-10 max-w-2xl">
              <h2 id="pricing-title" className="text-balance text-3xl font-semibold tracking-tight text-slate-950">{t("pricing.title")}</h2>
              <p className={`mt-2 ${soft}`}>{t("pricing.subtitle")}</p>
            </div>
            <div className="grid gap-5 md:grid-cols-3">
              {PLAN_KEYS.map((key) => (
                <article
                  key={key}
                  data-reveal
                  className={key === "pro" ? "relative rounded-2xl border-2 border-slate-950 bg-white p-6 shadow-[0_24px_60px_-30px_rgba(2,6,23,0.45)]" : `rounded-2xl border ${line} bg-white p-6`}
                >
                  {key === "pro" ? (
                    <span className="absolute -top-3 left-5 rounded-full bg-sky-600 px-3 py-0.5 text-xs font-medium text-white">
                      {t("pricing.pro.badge")}
                    </span>
                  ) : null}
                  <h3 className="text-lg font-semibold text-slate-950">{t(`pricing.${key}.name`)}</h3>
                  <p className="mt-3 flex items-baseline gap-1.5">
                    <span className="text-4xl font-semibold tabular-nums tracking-tight text-slate-950">{t(`pricing.${key}.price`)}</span>
                    <span className={`text-sm ${soft}`}>{t("pricing.perMonth")}</span>
                  </p>
                  <p className={`mt-1 text-sm ${soft}`}>{t("pricing.users", { count: PLAN_USERS[key] })}</p>
                  <ul className="mt-5 space-y-2 text-sm">
                    {(["f1", "f2", "f3"] as const).map((feature) => (
                      <li key={feature} className="flex items-start gap-2">
                        <Check className="mt-0.5 size-4 shrink-0 text-sky-600" aria-hidden />
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

        <section id="faq" aria-labelledby="faq-title" className="mx-auto max-w-3xl px-4 py-20">
          <h2 id="faq-title" data-reveal className="text-balance text-3xl font-semibold tracking-tight text-slate-950">{t("faq.title")}</h2>
          <div className={`mt-8 divide-y divide-slate-200 rounded-2xl border ${line} bg-white`}>
            {FAQ_KEYS.map((key) => (
              <details key={key} data-reveal className="group px-5 py-4">
                <summary className="cursor-pointer list-none font-medium marker:content-none">
                  <span className="flex items-center justify-between gap-4">
                    {t(`faq.q${key}`)}
                    <ArrowRight className="size-4 shrink-0 text-slate-400 transition group-open:rotate-90" aria-hidden />
                  </span>
                </summary>
                <p className={`mt-3 text-sm leading-relaxed ${soft}`}>{t(`faq.a${key}`)}</p>
              </details>
            ))}
          </div>
        </section>

        <section aria-labelledby="cta-title" className="mx-auto max-w-6xl px-4 pb-24">
          <div data-reveal className="relative overflow-hidden rounded-3xl bg-slate-950 px-6 py-14 text-center text-white md:px-12">
            <div aria-hidden className="pointer-events-none absolute -right-24 -top-24 h-72 w-72 rounded-full bg-sky-500/30 blur-3xl" />
            <div aria-hidden className="pointer-events-none absolute -bottom-32 -left-16 h-72 w-72 rounded-full bg-sky-400/20 blur-3xl" />
            <h2 id="cta-title" className="relative text-balance text-3xl font-semibold tracking-tight">{t("cta.title")}</h2>
            <p className="relative mt-3 text-slate-300">{t("cta.body")}</p>
            <Link
              href="/signup"
              className="relative mt-8 inline-flex items-center gap-2 rounded-xl bg-white px-6 py-3 text-sm font-medium text-slate-950 transition hover:bg-slate-100"
            >
              {t("cta.button")}
              <ArrowRight className="size-4" aria-hidden />
            </Link>
          </div>
        </section>
      </main>

      <footer className={`border-t ${line}`}>
        <div className={`mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-4 px-4 py-8 text-sm ${soft}`}>
          <span>{t("footer.tagline")}</span>
          <div className="flex gap-5">
            <Link href="/login" className="transition hover:text-slate-900">{t("footer.login")}</Link>
            <Link href="/signup" className="transition hover:text-slate-900">{t("footer.signup")}</Link>
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
      <div className="mt-2 font-mono text-sm text-slate-950">{doc}</div>
      {sub ? <div className={`text-xs ${soft}`}>{sub}</div> : null}
    </div>
  );
}

const toneClasses = {
  ok: "bg-emerald-50 text-emerald-700 ring-1 ring-inset ring-emerald-200",
  warn: "bg-amber-50 text-amber-700 ring-1 ring-inset ring-amber-200",
  bad: "bg-red-50 text-red-700 ring-1 ring-inset ring-red-200",
  info: "bg-sky-50 text-sky-700 ring-1 ring-inset ring-sky-200",
  muted: "bg-slate-100 text-slate-600",
} as const;

/** Small status pill used inside the demo panels. */
function Tag({ tone, children }: { tone: keyof typeof toneClasses; children: ReactNode }) {
  return <span className={`inline-block rounded-full px-2 py-0.5 text-[11px] font-medium ${toneClasses[tone]}`}>{children}</span>;
}
