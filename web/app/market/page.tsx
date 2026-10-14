import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { Activity, ArrowRight, Radar } from "lucide-react";
import { getMarketModels, marketHref } from "@/lib/api";
import { money } from "@/lib/format";

export const metadata: Metadata = {
  title: "Market",
  description: "What Japanese auction cars open at, model by model: typical opening bids, the usual range and what's at auction now.",
};

export default function MarketIndex() {
  return (
    <div className="space-y-10">
      <section className="relative isolate left-1/2 w-screen -translate-x-1/2 overflow-hidden pt-12 pb-8">
        <div className="grid-bg absolute inset-0 -z-10 opacity-70" />
        <div className="mx-auto max-w-6xl px-4 sm:px-6">
          <span className="inline-flex items-center gap-2 rounded-full border border-accent/50 bg-accent/10 px-4 py-1.5 text-[11px] font-semibold tracking-[0.2em] text-accent uppercase">
            <Activity className="size-3.5" aria-hidden /> Japanese auction market
          </span>
          <h1 className="display-xl mt-5">
            The <span className="glow-red">market</span>
          </h1>
          <p className="mt-4 max-w-2xl text-muted">
            What each car opens at in Japan, how that moves over time, and what&apos;s at auction right now. Built from every lot Mitsuke watches, one row
            per car. Opening bids, not sale prices: cars usually sell for more.
          </p>
        </div>
      </section>
      <Suspense fallback={<div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">{[0, 1, 2].map((i) => <div key={i} className="card h-44 animate-pulse" />)}</div>}>
        <Models />
      </Suspense>
    </div>
  );
}

async function Models() {
  const models = await getMarketModels();
  if (models.length === 0) {
    return (
      <div className="card p-10 text-center">
        <Radar className="mx-auto size-10 text-accent" aria-hidden />
        <p className="mt-4 text-muted">Not enough history yet. Pages appear as Mitsuke sees enough cars of a model.</p>
      </div>
    );
  }
  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {models.map((m) => (
        <Link key={`${m.make}-${m.code}`} href={marketHref(m.make, m.code)} className="card group relative overflow-hidden p-5 transition duration-200 hover:-translate-y-1 hover:border-white/15">
          <div className="pointer-events-none absolute inset-0 grid-bg opacity-30" aria-hidden />
          <div className="relative">
            <div className="flex items-center justify-between gap-3">
              <span className="rounded-md border border-accent/60 px-2 py-0.5 font-mono text-xs font-bold tracking-wider text-accent">{m.code}</span>
              {m.onAuctionNow > 0 && (
                <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-good">
                  <span className="size-1.5 animate-pulse rounded-full bg-good" /> {m.onAuctionNow} at auction
                </span>
              )}
            </div>
            <p className="display mt-3 text-2xl">{m.name}</p>
            <p className="text-sm text-muted">
              {m.make}
              {m.yearFrom && ` · ${m.yearFrom}–${m.yearTo}`}
            </p>
            <div className="mt-4 flex items-end justify-between border-t border-hairline pt-4">
              <div>
                <p className="label">Typically opens</p>
                <p className="figure mt-1 text-2xl">{money(m.typical, { compact: true })}</p>
              </div>
              <p className="inline-flex items-center gap-1 text-xs text-faint">
                {m.cars} cars <ArrowRight className="size-3.5 transition group-hover:translate-x-1" aria-hidden />
              </p>
            </div>
          </div>
        </Link>
      ))}
    </div>
  );
}
