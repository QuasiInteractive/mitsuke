import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { ArrowLeft, BellPlus, CalendarDays, Gauge, History, TrendingDown, TrendingUp, Minus } from "lucide-react";
import { getMarketModel, type LotCard, type MarketModelPage } from "@/lib/api";
import { HISTORY } from "@/lib/history";
import { day, km, money } from "@/lib/format";
import { HistoryChart } from "@/components/HistoryChart";
import { CardStrip } from "@/components/CardStrip";

export async function generateMetadata({ params }: PageProps<"/market/[make]/[code]">): Promise<Metadata> {
  const { make, code } = await params;
  const page = await getMarketModel(decodeURIComponent(make), decodeURIComponent(code)).catch(() => null);
  const name = page?.model.name ?? decodeURIComponent(code).toUpperCase();
  return {
    title: `${name} market`,
    description: page
      ? `What a ${name} (${page.model.code}) opens at in Japanese auctions: typically ${money(page.model.typical, { compact: true })}, the usual range and what's at auction now.`
      : undefined,
  };
}

export default function MarketModel({ params }: PageProps<"/market/[make]/[code]">) {
  return (
    <Suspense fallback={<div className="card mt-10 h-96 animate-pulse" />}>
      {params.then(({ make, code }) => <Content make={decodeURIComponent(make)} code={decodeURIComponent(code)} />)}
    </Suspense>
  );
}

async function Content({ make, code }: { make: string; code: string }) {
  const page = await getMarketModel(make, code);
  if (!page) notFound();
  const m = page.model;
  const meter = page.meter;
  const months = page.months;
  const lowest = months.length ? Math.min(...months.map((x) => x.min)) : meter ? Math.min(...meter.spread) : null;
  const highest = months.length ? Math.max(...months.map((x) => x.max)) : meter ? Math.max(...meter.spread) : null;
  const history = HISTORY[m.code.toUpperCase()];

  return (
    <div className="space-y-8">
      <section className="relative isolate left-1/2 w-screen -translate-x-1/2 overflow-hidden pt-10 pb-6">
        <div className="grid-bg absolute inset-0 -z-10 opacity-70" />
        <div className="mx-auto max-w-6xl px-4 sm:px-6">
          <Link href="/market" className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-text">
            <ArrowLeft className="size-4" aria-hidden /> Market
          </Link>
          <div className="mt-5 flex flex-wrap items-end justify-between gap-6">
            <div>
              <span className="rounded-md border border-accent/60 px-2 py-0.5 font-mono text-xs font-bold tracking-wider text-accent">{m.code}</span>
              <h1 className="display-xl mt-4">{m.name}</h1>
              <p className="mt-2 text-muted">
                {m.make} {m.model}
                {m.yearFrom && ` · built ${m.yearFrom}–${m.yearTo}`}
              </p>
            </div>
            <div className="text-right">
              <p className="label">Typically opens at</p>
              <p className="figure glow-red text-6xl">{money(m.typical, { compact: true })}</p>
              <p className="mt-1 text-sm text-muted">
                {m.onAuctionNow > 0 ? <span className="text-good">{m.onAuctionNow} at auction now</span> : "none at auction right now"} · {m.cars} cars seen
              </p>
            </div>
          </div>
        </div>
      </section>

      {history && <ThenAndNow page={page} />}

      <section className="card p-5 sm:p-6">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="flex items-center gap-3 text-lg font-semibold">
            <span className="grid size-9 place-items-center rounded-xl border border-hairline bg-black/30 text-muted">
              <History className="size-[18px]" aria-hidden />
            </span>
            Opening bids over time
          </h2>
          <span className="text-xs text-faint">since {day(m.since)}</span>
        </div>
        <div className="mt-5">
          <HistoryChart months={months} meter={meter} />
        </div>
        <dl className="mt-5 grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Stat label="Usual range" value={meter ? `${money({ amount: meter.low, currency: "JPY" }, { compact: true })}–${money({ amount: meter.high, currency: "JPY" }, { compact: true }).replace("¥", "")}` : "—"} />
          <Stat label="Cheapest seen" value={lowest != null ? money({ amount: lowest, currency: "JPY" }, { compact: true }) : "—"} />
          <Stat label="Dearest seen" value={highest != null ? money({ amount: highest, currency: "JPY" }, { compact: true }) : "—"} />
          <Stat label="Trend" value={<Trend percent={meter?.trendPercent ?? null} />} />
        </dl>
        <p className="mt-4 text-xs text-faint">
          {page.basis} Trends and seasons need history: they fill in as Mitsuke watches, and a longer archive is on the way. Mitsuke doesn&apos;t
          forecast prices: a trend says where things have been heading, not where they will go.
        </p>
      </section>

      <section>
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <p className="label text-good">At auction now</p>
            <h2 className="display mt-2 text-3xl">{page.live.length > 0 ? `${page.live.length} you can look at` : "Nothing right now"}</h2>
          </div>
          <Link href="/watchlists/new" className="btn-primary px-5 py-3">
            <BellPlus className="size-4" aria-hidden /> Watch for one
          </Link>
        </div>
        {page.live.length > 0 ? (
          <div className="mt-5">
            <CardStrip count={page.live.length} label="cars at auction">
              {page.live.map((c) => <LiveCard key={c.id} card={c} />)}
            </CardStrip>
          </div>
        ) : (
          <p className="card mt-5 p-6 text-sm text-muted">New lots land every day. A watchlist alerts you the moment one appears.</p>
        )}
      </section>
    </div>
  );
}

function ThenAndNow({ page }: { page: MarketModelPage }) {
  const h = HISTORY[page.model.code.toUpperCase()]!;
  const first = h.launch[0];
  const ratio = first ? Math.round((page.model.typical.amount / first.priceJpy) * 100) : null;
  return (
    <section className="card relative overflow-hidden p-5 sm:p-6">
      <div className="grid gap-6 md:grid-cols-[1.2fr_1fr]">
        <div>
          <p className="label text-accent">Then and now</p>
          <p className="mt-3 text-lg leading-relaxed">{h.story}</p>
          {h.production && <p className="mt-2 text-sm text-muted">{h.production}</p>}
        </div>
        <div className="space-y-3">
          {h.launch.map((l) => (
            <div key={l.grade} className="flex items-end justify-between gap-3 rounded-2xl border border-hairline bg-black/25 px-4 py-3">
              <div>
                <p className="text-sm font-semibold">{l.grade}</p>
                <p className="flex items-center gap-1 text-xs text-faint"><CalendarDays className="size-3" aria-hidden /> new in {new Date(l.launched + "-01T00:00:00Z").toLocaleDateString("en-AU", { month: "short", year: "numeric", timeZone: "UTC" })}</p>
              </div>
              <div className="text-right">
                <p className="figure text-xl">{money({ amount: l.priceJpy, currency: "JPY" })}</p>
                <a href={l.source.url} target="_blank" rel="noopener noreferrer" className="text-[11px] text-faint hover:text-text">{l.taxNote} · {l.source.label} ↗</a>
              </div>
            </div>
          ))}
          {ratio != null && (
            <p className="text-sm text-muted">
              Today a typical one opens at <span className="text-text">{money(page.model.typical, { compact: true })}</span>, about{" "}
              <span className="figure text-accent">{ratio}%</span> of its price new, before yen inflation since.
            </p>
          )}
        </div>
      </div>
    </section>
  );
}

function Stat({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="rounded-2xl border border-hairline bg-black/25 px-4 py-3">
      <dt className="label">{label}</dt>
      <dd className="figure mt-1 text-xl">{value}</dd>
    </div>
  );
}

function Trend({ percent }: { percent: number | null }) {
  if (percent == null) return <span className="text-sm text-faint not-italic">building</span>;
  const Icon = percent > 2 ? TrendingUp : percent < -2 ? TrendingDown : Minus;
  return (
    <span className={`inline-flex items-center gap-1 ${percent < -2 ? "text-good" : percent > 2 ? "text-amber" : ""}`}>
      <Icon className="size-4" aria-hidden /> {percent > 0 ? "+" : ""}
      {percent.toFixed(0)}%
    </span>
  );
}

function LiveCard({ card: c }: { card: LotCard }) {
  return (
    <Link href={`/lot/${c.id}`} className="card group w-72 shrink-0 overflow-hidden transition duration-200 hover:-translate-y-1 hover:border-white/15">
      <div className="relative aspect-[16/10] bg-black">
        <div className="grid-bg absolute inset-0 opacity-50" aria-hidden />
        {c.photo && (
          // eslint-disable-next-line @next/next/no-img-element -- remote auction photos via signed URLs
          <img src={c.photo} alt="" className="absolute inset-0 size-full object-cover opacity-90 transition group-hover:opacity-100" loading="lazy" />
        )}
        <div className="absolute inset-0 bg-gradient-to-t from-black/80 to-transparent" />
        {c.dealScore != null && (
          <span className={`tabular absolute top-3 right-3 rounded-full px-2.5 py-1 text-xs font-bold backdrop-blur ${c.dealScore >= 80 ? "bg-accent text-white" : "bg-black/60 text-white/90"}`}>
            {c.dealScore}/100
          </span>
        )}
        <p className="absolute inset-x-4 bottom-3 truncate font-semibold">{c.title}</p>
      </div>
      <div className="p-4">
        <p className="flex flex-wrap items-center gap-x-2 text-xs text-muted">
          <span className="inline-flex items-center gap-1"><Gauge className="size-3" aria-hidden />{km(c.mileageKm)}</span>
          <span>grade {c.grade ?? "—"}</span>
          {c.auctionDay && <span>{day(c.auctionDay)}</span>}
        </p>
        <div className="mt-3 flex items-end justify-between">
          <div>
            <p className="label">Est. landed</p>
            <p className="figure mt-1 text-xl">{money(c.landedTotal)}</p>
          </div>
          <p className="tabular text-xs text-faint">{money(c.openingBid, { compact: true })} opening</p>
        </div>
      </div>
    </Link>
  );
}
