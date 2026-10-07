import { Suspense } from "react";
import Link from "next/link";
import { getMatches, getWatchlists, type LotCard } from "@/lib/api";
import { day, km, money } from "@/lib/format";

export default function Home() {
  return (
    <div className="space-y-8">
      <section className="pt-4">
        <h1 className="text-3xl font-bold tracking-tight sm:text-4xl">Your matches</h1>
        <p className="mt-2 max-w-xl text-muted">
          Cars at Japanese auctions that fit your watchlists, with the cost on the ground and what the auction sheet really says.
        </p>
      </section>
      <Suspense fallback={<GridSkeleton />}>
        <Watchlists />
        <Matches />
      </Suspense>
    </div>
  );
}

async function Watchlists() {
  const lists = await getWatchlists();
  if (lists.length === 0) return null;
  return (
    <div className="no-scrollbar flex gap-3 overflow-x-auto">
      {lists.map(({ watchlist: w, matchCount }) => (
        <div key={w.id} className="card shrink-0 px-4 py-3">
          <p className="font-semibold">{w.name}</p>
          <p className="text-sm text-muted">
            {w.maxLanded ? `under ${money(w.maxLanded)} landed` : `${w.make} ${w.model}`} · {matchCount} match{matchCount === 1 ? "" : "es"}
          </p>
        </div>
      ))}
    </div>
  );
}

async function Matches() {
  const cards = await getMatches();
  if (cards.length === 0) {
    return <p className="card p-8 text-center text-muted">No matches yet. Mitsuke checks the auctions every ten minutes.</p>;
  }
  return (
    <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {cards.map((c) => <Card key={`${c.id}-${c.watchlistName}`} card={c} />)}
    </div>
  );
}

function Card({ card: c }: { card: LotCard }) {
  return (
    <Link href={`/lot/${c.id}`} className="card group overflow-hidden transition hover:-translate-y-0.5 hover:border-accent/70">
      <div className="relative bg-black">
        {c.photo ? (
          // eslint-disable-next-line @next/next/no-img-element -- remote auction photos via redirecting, signed URLs
          <img src={c.photo} alt={c.title} className="aspect-[16/10] w-full object-cover opacity-90 transition group-hover:opacity-100" loading="lazy" />
        ) : (
          <div className="aspect-[16/10]" />
        )}
        {c.dealScore != null && (
          <span className={`tabular absolute top-3 right-3 rounded-full px-2.5 py-1 text-xs font-semibold backdrop-blur ${c.dealScore >= 80 ? "bg-accent/90" : "bg-black/70"}`}>
            {c.dealScore}/100
          </span>
        )}
        {c.highFlags > 0 && (
          <span className="absolute top-3 left-3 rounded-full bg-black/75 px-2.5 py-1 text-xs font-semibold text-accent backdrop-blur">⚠ Check sheet</span>
        )}
      </div>
      <div className="p-4">
        <p className="font-semibold">{c.title}</p>
        <p className="text-sm text-muted">
          {km(c.mileageKm)} · grade {c.grade ?? "—"} · {day(c.auctionDay)}
        </p>
        <div className="mt-3 flex items-end justify-between">
          <div>
            <p className="text-xs text-faint">Est. landed</p>
            <p className="tabular text-xl font-bold">{money(c.landedTotal)}</p>
          </div>
          <p className="tabular text-sm text-muted">{money(c.openingBid, { compact: true })} bid</p>
        </div>
      </div>
    </Link>
  );
}

function GridSkeleton() {
  return (
    <div className="grid animate-pulse gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {[0, 1, 2].map((i) => <div key={i} className="h-72 rounded-[1.25rem] bg-raised" />)}
    </div>
  );
}
