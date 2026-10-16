import { Suspense } from "react";
import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";
import { CalendarRange, Gauge, Pause, Pencil, Play, Plus, Star, Trash2, Wallet, Radar, type LucideIcon } from "lucide-react";
import { getUser } from "@/lib/supabase/server";
import { getMyCloseMatches, getMyWatchlistMarket, getMyWatchlistMatches, getMyWatchlists, type LotCard, type MyWatchlist } from "@/lib/api";
import { day, km, money } from "@/lib/format";
import { deleteWatchlist, setWatchlistActive } from "@/app/actions";
import { PushToggle } from "@/components/PushToggle";
import { MarketStrip } from "@/components/MarketMeter";
import { CardStrip } from "@/components/CardStrip";

export const metadata: Metadata = { title: "Your watchlists" };

export default function WatchlistsPage() {
  return (
    <div className="space-y-8">
      <section className="relative isolate left-1/2 w-screen -translate-x-1/2 overflow-hidden pt-12 pb-8">
        <div className="grid-bg absolute inset-0 -z-10 opacity-70" />
        <div className="mx-auto flex max-w-6xl flex-wrap items-end justify-between gap-6 px-4 sm:px-6">
          <div>
            <span className="inline-flex items-center gap-2 rounded-full border border-accent/50 bg-accent/10 px-4 py-1.5 text-[11px] font-semibold tracking-[0.2em] text-accent uppercase">
              <span className="size-1.5 animate-pulse rounded-full bg-accent shadow-[0_0_8px_var(--color-accent)]" /> Watching every hour
            </span>
            <h1 className="display-xl mt-5">
              The <span className="glow-red">hunt</span>
            </h1>
            <p className="mt-4 max-w-xl text-muted">Every car you&apos;re after, what its market is doing, and what&apos;s at auction that nearly fits.</p>
          </div>
          <Link href="/watchlists/new" className="btn-primary underglow shrink-0 px-6 py-4">
            <Plus className="size-5" aria-hidden /> New watchlist
          </Link>
        </div>
      </section>
      <PushToggle />
      <Suspense fallback={<div className="card h-64 animate-pulse" />}>
        <List />
      </Suspense>
    </div>
  );
}

async function List() {
  const user = await getUser();
  if (!user) redirect("/login");
  const lists = await getMyWatchlists(user.accessToken);

  if (lists.length === 0) {
    return (
      <div className="card relative overflow-hidden p-10 text-center">
        <div className="grid-bg absolute inset-0 opacity-50" aria-hidden />
        <div className="relative">
          <Radar className="mx-auto size-10 text-accent" aria-hidden />
          <p className="display mt-4 text-3xl">Nothing on the radar</p>
          <p className="mt-2 text-muted">Tell Mitsuke the car you want and your budget, landed.</p>
          <Link href="/watchlists/new" className="btn-primary mt-6 px-6 py-3">Create your first watchlist</Link>
        </div>
      </div>
    );
  }
  return (
    <div className="space-y-6">
      {lists.map((w) => (
        <Row key={w.watchlist.id} item={w} token={user.accessToken} />
      ))}
    </div>
  );
}

function Row({ item, token }: { item: MyWatchlist; token: string }) {
  const w = item.watchlist;
  const budget = w.maxPrice ? `Opens under ${money(w.maxPrice)}` : w.maxLanded ? `Under ${money(w.maxLanded)} landed ${w.destination}` : null;
  const chips: [LucideIcon, string | null | false | 0 | undefined][] = [
    [CalendarRange, (w.yearFrom || w.yearTo) && `${w.yearFrom ?? "…"}–${w.yearTo ?? "…"}`],
    [Gauge, w.maxMileageKm && `Under ${km(w.maxMileageKm)}`],
    [Star, w.minGrade && `Grade ${w.minGrade}+`],
    [Wallet, budget],
  ];

  return (
    <section className={`card relative overflow-hidden ${w.isActive ? "" : "opacity-60"}`}>
      {w.isActive && <div className="absolute inset-y-0 left-0 w-1 bg-accent shadow-[0_0_18px_var(--color-accent)]" aria-hidden />}
      <div className="p-6 sm:p-7">
        <div className="flex flex-wrap items-start justify-between gap-5">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              {w.modelCodes.map((c) => (
                <span key={c} className="rounded-md border border-accent/60 px-2 py-0.5 font-mono text-xs font-bold tracking-wider text-accent">{c}</span>
              ))}
              <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold ${w.isActive ? "bg-good/15 text-good" : "bg-white/5 text-muted"}`}>
                <span className={`size-1.5 rounded-full ${w.isActive ? "animate-pulse bg-good" : "bg-muted"}`} />
                {w.isActive ? "Watching" : "Paused"}
              </span>
            </div>
            <h2 className="display mt-3 text-3xl sm:text-4xl">{w.name}</h2>
            <p className="mt-1 text-sm text-muted">{w.make} {w.model}</p>
            <div className="mt-4 flex flex-wrap gap-2">
              {chips.filter(([, t]) => t).map(([Icon, t]) => (
                <span key={String(t)} className="inline-flex items-center gap-1.5 rounded-full border border-hairline bg-white/[0.03] px-3 py-1 text-xs">
                  <Icon className="size-3.5 text-muted" aria-hidden /> {t}
                </span>
              ))}
            </div>
          </div>
          <div className="text-right">
            <p className={`figure text-6xl ${item.matchCount > 0 ? "glow-red" : "text-muted"}`}>{item.matchCount}</p>
            <p className="label">match{item.matchCount === 1 ? "" : "es"}</p>
          </div>
        </div>

        {item.matchCount > 0 && (
          <Suspense fallback={<div className="mt-6 h-56 animate-pulse rounded-2xl bg-white/[0.03]" />}>
            <Matches id={w.id} token={token} />
          </Suspense>
        )}

        <Suspense fallback={<div className="mt-5 h-24 animate-pulse rounded-2xl bg-white/[0.03]" />}>
          <Market id={w.id} token={token} />
        </Suspense>

        {w.isActive && (
          <Suspense fallback={<div className="mt-5 h-40 animate-pulse rounded-2xl bg-white/[0.03]" />}>
            <CloseMatches id={w.id} token={token} />
          </Suspense>
        )}

        <div className="mt-6 flex flex-wrap gap-2 border-t border-hairline pt-4">
          <Link href={`/watchlists/${w.id}/edit`} className="btn-ghost"><Pencil className="size-4" aria-hidden /> Edit</Link>
          <form action={setWatchlistActive.bind(null, w.id, !w.isActive)}>
            <button className="btn-ghost">
              {w.isActive ? <Pause className="size-4" aria-hidden /> : <Play className="size-4" aria-hidden />}
              {w.isActive ? "Pause" : "Resume"}
            </button>
          </form>
          <form action={deleteWatchlist.bind(null, w.id)}>
            <button className="btn-ghost hover:!border-accent/60 hover:!text-accent"><Trash2 className="size-4" aria-hidden /> Delete</button>
          </form>
        </div>
      </div>
    </section>
  );
}

/** Today (or a day ahead) in Japan as YYYY-MM-DD: auction days are Japanese dates. Server-rendered per request. */
function jstDay(offsetDays: number): string {
  return new Date(Date.now() + 9 * 3600_000 + offsetDays * 86_400_000).toISOString().slice(0, 10);
}

/** Where a lot is in its life: bids close about a day before the auction day (Japan). */
function status(card: LotCard, today: string, tomorrow: string): { text: string; tone: string; open: boolean } {
  if (!card.auctionDay) return { text: "Date to come", tone: "text-faint", open: true };
  if (card.auctionDay < today) return { text: "Auction over", tone: "text-faint", open: false };
  if (card.auctionDay <= tomorrow) return { text: "Bidding closed", tone: "text-amber", open: false };
  return { text: "Bids open", tone: "text-good", open: true };
}

/** The cars this watchlist has alerted on, still-open ones first. */
async function Matches({ id, token }: { id: string; token: string }) {
  const cards = await getMyWatchlistMatches(token, id);
  if (!cards || cards.length === 0) return null;
  const [today, tomorrow] = [jstDay(0), jstDay(1)];
  const rows = cards.map((c) => ({ c, s: status(c, today, tomorrow) }));
  rows.sort((a, b) => Number(b.s.open) - Number(a.s.open));
  const open = rows.filter((r) => r.s.open).length;

  return (
    <div className="mt-6">
      <p className="label">
        Your matches · <span className="text-good">{open} open for bids</span>
        {rows.length > open && <span className="text-faint"> · {rows.length - open} gone to auction</span>}
      </p>
      <div className="mt-3">
      <CardStrip count={rows.length} label="your matches">
        {rows.map(({ c, s }) => (
          <Link
            key={c.id}
            href={`/lot/${c.id}`}
            className={`group w-72 shrink-0 overflow-hidden rounded-2xl border bg-black/30 transition duration-200 hover:-translate-y-0.5 ${s.open ? "border-accent/40 hover:border-accent" : "border-hairline opacity-70 hover:opacity-100"}`}
          >
            <div className="relative aspect-[16/10] bg-raised">
              <div className="grid-bg absolute inset-0 opacity-60" aria-hidden />
              {c.photo && (
                // Empty alt: once the auction's over the photo is often removed; then nothing shows rather than a broken icon.
                // eslint-disable-next-line @next/next/no-img-element -- auction photos come from the provider's CDN
                <img src={c.photo} alt="" className="absolute inset-0 size-full object-cover opacity-90 transition group-hover:opacity-100" loading="lazy" />
              )}
              <div className="absolute inset-0 bg-gradient-to-t from-black/85 via-black/10 to-transparent" />
              {c.dealScore != null && (
                <span className={`tabular absolute top-3 right-3 rounded-full px-2.5 py-1 text-xs font-bold backdrop-blur ${c.dealScore >= 80 ? "bg-accent text-white" : "bg-black/60 text-white/90"}`}>
                  {c.dealScore}/100
                </span>
              )}
              <div className="absolute inset-x-3 bottom-2.5">
                <p className="truncate font-semibold">{c.title}</p>
                <p className="text-xs text-white/65">
                  {[c.mileageKm != null && km(c.mileageKm), c.grade && `grade ${c.grade}`, c.auctionDay && day(c.auctionDay)].filter(Boolean).join(" · ")}
                </p>
              </div>
            </div>
            <div className="flex items-end justify-between p-3.5">
              <p className="figure text-2xl">
                {money(c.landedTotal)} <span className="font-sans text-xs font-normal text-faint not-italic">landed</span>
              </p>
              <span className={`inline-flex items-center gap-1.5 text-xs font-semibold ${s.tone}`}>
                <span className={`size-1.5 rounded-full ${s.open ? "animate-pulse bg-good" : "bg-current"}`} />
                {s.text}
              </span>
            </div>
          </Link>
        ))}
      </CardStrip>
      </div>
    </div>
  );
}

async function Market({ id, token }: { id: string; token: string }) {
  const meter = await getMyWatchlistMarket(token, id);
  if (!meter) return null; // too few cars seen yet to say anything honest
  return (
    <div className="mt-5">
      <MarketStrip meter={meter} />
    </div>
  );
}

/** Cars at auction now that nearly fit, and what they miss by: so a quiet watchlist still shows the market. */
async function CloseMatches({ id, token }: { id: string; token: string }) {
  // Nice to have, never essential: if it fails, the watchlist still renders.
  const close = await getMyCloseMatches(token, id).catch(() => null);
  if (close === null) {
    return <p className="mt-5 rounded-2xl border border-hairline bg-black/20 px-4 py-3 text-sm text-faint">Couldn&apos;t load what&apos;s at auction right now. Try again in a minute.</p>;
  }
  if (close.length === 0) {
    return <p className="mt-5 rounded-2xl border border-hairline bg-black/20 px-4 py-3 text-sm text-faint">Nothing close at auction right now. New lots land every day.</p>;
  }
  return (
    <div className="mt-6">
      <p className="label">Close, but not quite · <span className="text-amber">{close.length} at auction now</span></p>
      <div className="mt-3">
      <CardStrip count={close.length} label="close matches">
        {close.map(({ lot, missesBy }) => (
          <Link key={lot.id} href={`/lot/${lot.id}`} className="group w-72 shrink-0 overflow-hidden rounded-2xl border border-hairline bg-black/30 transition duration-200 hover:-translate-y-0.5 hover:border-white/20">
            <div className="relative aspect-[16/10] bg-raised">
              {lot.photo && (
                // eslint-disable-next-line @next/next/no-img-element -- auction photos come from the provider's CDN
                <img src={lot.photo} alt="" className="size-full object-cover opacity-90 transition group-hover:opacity-100" loading="lazy" />
              )}
              <div className="absolute inset-0 bg-gradient-to-t from-black/85 via-black/10 to-transparent" />
              <div className="absolute inset-x-3 bottom-2.5">
                <p className="truncate font-semibold">{lot.title}</p>
                <p className="text-xs text-white/65">
                  {[lot.mileageKm != null && km(lot.mileageKm), lot.grade && `grade ${lot.grade}`, lot.auctionDay && day(lot.auctionDay)].filter(Boolean).join(" · ")}
                </p>
              </div>
            </div>
            <div className="p-3.5">
              {lot.landedTotal && (
                <p className="figure text-2xl">
                  {money(lot.landedTotal)} <span className="font-sans text-xs font-normal text-faint not-italic">landed</span>
                </p>
              )}
              <div className="mt-2 flex flex-wrap gap-1.5">
                {missesBy.map((m) => (
                  <span key={m} className="rounded-full border border-amber/40 bg-amber/10 px-2.5 py-0.5 text-[11px] text-amber">{m}</span>
                ))}
              </div>
            </div>
          </Link>
        ))}
      </CardStrip>
      </div>
    </div>
  );
}
