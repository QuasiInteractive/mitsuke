import Link from "next/link";
import { ArrowRight, CalendarDays, Flame, Gauge, TrendingDown } from "lucide-react";
import { getMarketPicks, type MarketPick } from "@/lib/api";
import { day, km, money } from "@/lib/format";

/**
 * The top of the home page: the month's best find and what's cheap to start right now, from Mitsuke's own market
 * scan. Hidden entirely when the scan has nothing confident to say.
 */
export async function MarketPicks() {
  const picks = await getMarketPicks();
  if (!picks || (!picks.steal && picks.cheapNow.length === 0)) return null;
  const rest = picks.cheapNow.filter((p) => p.lot.id !== picks.steal?.lot.id);

  return (
    <section className="space-y-6">
      {picks.steal && <Steal pick={picks.steal} month={picks.month} />}
      {rest.length > 0 && (
        <div>
          <div className="flex flex-wrap items-end justify-between gap-3">
            <div>
              <p className="label flex items-center gap-1.5 text-amber"><Flame className="size-3.5" aria-hidden /> Cheap right now</p>
              <h2 className="display mt-2 text-3xl">Opening low, bids open</h2>
            </div>
            <p className="max-w-sm text-xs text-faint">{picks.basis}</p>
          </div>
          <div className="no-scrollbar -mx-1 mt-5 flex snap-x gap-4 overflow-x-auto px-1 pb-2">
            {rest.map((p) => <PickCard key={p.lot.id} pick={p} />)}
          </div>
        </div>
      )}
    </section>
  );
}

function Steal({ pick: p, month }: { pick: MarketPick; month: string }) {
  const c = p.lot;
  return (
    <Link href={`/lot/${c.id}`} className="card card-alert underglow group relative grid overflow-hidden md:grid-cols-[1.2fr_1fr]">
      <div className="relative min-h-64 bg-black">
        {c.photo && (
          // eslint-disable-next-line @next/next/no-img-element -- remote auction photos via signed URLs
          <img src={c.photo} alt={c.title} className="absolute inset-0 size-full object-cover transition duration-700 group-hover:scale-[1.03]" />
        )}
        <div className="absolute inset-0 bg-gradient-to-t from-black/80 via-transparent to-transparent md:bg-gradient-to-r md:from-transparent md:via-transparent md:to-black/70" />
        <span className="hanko absolute top-5 left-5 bg-black/55 px-2.5 py-3 text-xl tracking-[0.15em] backdrop-blur [writing-mode:vertical-rl]">掘り出し物</span>
      </div>
      <div className="relative p-6 sm:p-8">
        <div className="grid-bg pointer-events-none absolute inset-0 opacity-50" aria-hidden />
        <div className="relative">
          <p className="label text-accent">Steal of the month · {month}</p>
          <p className="mt-3 text-2xl font-bold leading-tight">{c.title}</p>
          <p className="mt-1 flex flex-wrap gap-x-3 text-sm text-muted">
            <span className="inline-flex items-center gap-1"><Gauge className="size-3.5" aria-hidden />{km(c.mileageKm)}</span>
            <span>Grade {c.grade ?? "—"}</span>
            {c.auctionDay && <span className="inline-flex items-center gap-1"><CalendarDays className="size-3.5" aria-hidden />{day(c.auctionDay)}</span>}
          </p>
          <p className="figure glow-red mt-5 text-7xl leading-none">−{p.percentUnder}%</p>
          <p className="mt-2 text-sm text-muted">
            under the typical {p.group} opening bid of <span className="tabular text-text">{money(p.typical, { compact: true })}</span>, across {p.cars} cars
          </p>
          <div className="mt-5 grid grid-cols-2 gap-4 border-t border-hairline pt-4">
            <div>
              <p className="label">Opened at</p>
              <p className="figure mt-1 text-2xl">{money(c.openingBid, { compact: true })}</p>
            </div>
            <div>
              <p className="label">Est. landed</p>
              <p className="figure mt-1 text-2xl">{money(c.landedTotal)}</p>
            </div>
          </div>
          <p className={`mt-5 inline-flex items-center gap-2 text-sm font-semibold ${p.biddingOpen ? "text-good" : "text-faint"}`}>
            <span className={`size-2 rounded-full ${p.biddingOpen ? "animate-pulse bg-good" : "bg-faint"}`} />
            {p.biddingOpen ? "Bids still open" : "Gone to auction: set a watchlist to catch the next one"}
            <ArrowRight className="size-4 transition group-hover:translate-x-1" aria-hidden />
          </p>
        </div>
      </div>
    </Link>
  );
}

function PickCard({ pick: p }: { pick: MarketPick }) {
  const c = p.lot;
  return (
    <Link href={`/lot/${c.id}`} className="card group w-72 shrink-0 snap-start overflow-hidden transition duration-200 hover:-translate-y-1 hover:border-white/15">
      <div className="relative aspect-[16/10] bg-black">
        {c.photo && (
          // eslint-disable-next-line @next/next/no-img-element -- remote auction photos via signed URLs
          <img src={c.photo} alt={c.title} className="size-full object-cover opacity-90 transition group-hover:opacity-100" loading="lazy" />
        )}
        <div className="absolute inset-0 bg-gradient-to-t from-black/80 to-transparent" />
        <span className="absolute top-3 right-3 inline-flex items-center gap-1 rounded-full bg-good px-2.5 py-1 text-xs font-bold text-black shadow-[0_0_16px_rgb(53_224_122/0.5)]">
          <TrendingDown className="size-3.5" aria-hidden /> {p.percentUnder}% under
        </span>
        <p className="absolute inset-x-4 bottom-3 truncate font-semibold">{c.title}</p>
      </div>
      <div className="p-4">
        <p className="text-xs text-muted">{[km(c.mileageKm), `grade ${c.grade ?? "—"}`, c.auctionDay && day(c.auctionDay)].filter(Boolean).join(" · ")}</p>
        <div className="mt-3 flex items-end justify-between">
          <div>
            <p className="label">Est. landed</p>
            <p className="figure mt-1 text-xl">{money(c.landedTotal)}</p>
          </div>
          <p className="tabular text-right text-xs text-faint">
            {money(c.openingBid, { compact: true })} vs
            <br />
            usual {money(p.typical, { compact: true })}
          </p>
        </div>
      </div>
    </Link>
  );
}
