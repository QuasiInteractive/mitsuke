import { Suspense } from "react";
import Link from "next/link";
import { getMatches, getMyMatches, getMyWatchlists, getWatchlists, type LotCard } from "@/lib/api";
import { getUser } from "@/lib/supabase/server";
import { day, km, money } from "@/lib/format";

export default function Home() {
  return (
    <Suspense fallback={<GridSkeleton />}>
      <HomeContent />
    </Suspense>
  );
}

async function HomeContent() {
  const user = await getUser();
  if (!user) return <Landing />;

  const [cards, lists] = await Promise.all([getMyMatches(user.accessToken), getMyWatchlists(user.accessToken)]);
  return (
    <div className="space-y-8">
      <section className="pt-4">
        <h1 className="text-3xl font-bold tracking-tight sm:text-4xl">Your matches</h1>
        <p className="mt-2 max-w-xl text-muted">Cars that fit your watchlists, newest first. Mitsuke checks every ten minutes.</p>
      </section>
      {lists.length === 0 ? (
        <div className="card p-10 text-center">
          <p className="text-lg font-semibold">Start with the car you want</p>
          <p className="mt-1 text-muted">Pick a classic like an R32 GT-R or Supra, and set your budget landed.</p>
          <Link href="/watchlists/new" className="mt-6 inline-block rounded-xl bg-accent px-5 py-2.5 font-semibold">Create a watchlist</Link>
        </div>
      ) : (
        <>
          <div className="no-scrollbar flex gap-3 overflow-x-auto">
            {lists.map(({ watchlist: w, matchCount }) => (
              <Link key={w.id} href="/watchlists" className="card shrink-0 px-4 py-3 hover:border-accent/70">
                <p className="font-semibold">{w.name}</p>
                <p className="text-sm text-muted">{matchCount} match{matchCount === 1 ? "" : "es"}{w.maxLanded ? ` · under ${money(w.maxLanded)}` : ""}</p>
              </Link>
            ))}
            <Link href="/watchlists/new" className="card grid shrink-0 place-items-center px-5 py-3 text-muted hover:border-accent/70 hover:text-text">+ New</Link>
          </div>
          {cards.length === 0 ? (
            <p className="card p-8 text-center text-muted">Nothing matched yet. New lots appear at auction every week; Mitsuke will tell you.</p>
          ) : (
            <Grid cards={cards} />
          )}
        </>
      )}
    </div>
  );
}

async function Landing() {
  return (
    <div className="space-y-8">
      <section className="pt-6">
        <h1 className="max-w-2xl text-4xl font-bold tracking-tight sm:text-5xl">
          Your wishlist car, <span className="text-accent">found</span> at Japanese auction.
        </h1>
        <p className="mt-4 max-w-xl text-lg text-muted">
          Tell Mitsuke what you want and your budget on the ground. It watches the auctions around the clock and alerts you with the landed
          cost in Australia and a plain-English read of the auction sheet.
        </p>
        <Link href="/login" className="mt-6 inline-block rounded-2xl bg-accent px-6 py-3.5 font-semibold hover:bg-accent-strong">
          Start watching, free
        </Link>
      </section>
      <section>
        <h2 className="text-sm font-semibold tracking-wide text-faint uppercase">Live examples</h2>
        <Watchlists />
        <Matches />
      </section>
    </div>
  );
}

async function Watchlists() {
  const lists = await getWatchlists();
  if (lists.length === 0) return null;
  return (
    <div className="no-scrollbar mt-3 flex gap-3 overflow-x-auto">
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
  return <Grid cards={cards} />;
}

function Grid({ cards }: { cards: LotCard[] }) {
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
