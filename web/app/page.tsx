import { Suspense } from "react";
import Link from "next/link";
import { BellRing, Calculator, FileSearch, ShieldCheck, ArrowRight, Gauge, CalendarDays, TriangleAlert } from "lucide-react";
import { getMatches, getMyMatches, getMyWatchlists, getWatchlists, type LotCard } from "@/lib/api";
import { getUser } from "@/lib/supabase/server";
import { HeroVideo } from "@/components/HeroVideo";
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
    <div className="space-y-10 pt-10">
      <section className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <p className="label text-accent">Your matches</p>
          <h1 className="display mt-3">Found for you</h1>
          <p className="mt-3 max-w-xl text-muted">Cars that fit your watchlists, newest first. Mitsuke checks the Japanese auctions every ten minutes.</p>
        </div>
        <Link href="/watchlists/new" className="btn-primary px-6 py-3.5">+ New watchlist</Link>
      </section>

      {lists.length === 0 ? (
        <div className="card p-10 text-center">
          <p className="text-xl font-semibold">Start with the car you want</p>
          <p className="mt-2 text-muted">Pick a classic like an R32 GT-R or Supra, and set your budget landed.</p>
          <Link href="/watchlists/new" className="btn-primary mt-6 px-6 py-3">Create a watchlist</Link>
        </div>
      ) : (
        <>
          <div className="no-scrollbar -mx-1 flex gap-3 overflow-x-auto px-1">
            {lists.map(({ watchlist: w, matchCount }) => (
              <Link key={w.id} href="/watchlists" className="tile shrink-0 px-4 py-3">
                <span className="font-semibold">{w.name}</span>
                <span className="text-xs text-faint">
                  {matchCount} match{matchCount === 1 ? "" : "es"}{w.maxLanded ? ` · under ${money(w.maxLanded)}` : ""}
                </span>
              </Link>
            ))}
          </div>
          {cards.length === 0 ? (
            <p className="card p-8 text-center text-muted">Nothing matched yet. New lots appear at auction every day; your Watchlists page shows what&apos;s close.</p>
          ) : (
            <Grid cards={cards} />
          )}
        </>
      )}
    </div>
  );
}

const PROMISES = [
  { icon: BellRing, title: "Alerted in minutes", body: "Watches every Japanese auction around the clock and pings your phone the moment your car is listed." },
  { icon: Calculator, title: "The real landed cost", body: "Car, fees, shipping, duty, GST and compliance, in A$ or NZ$, before you bid." },
  { icon: FileSearch, title: "The sheet, in English", body: "The handwritten auction sheet decoded: grade, damage, mileage warnings." },
  { icon: ShieldCheck, title: "Can you import it?", body: "The 25-year rule and SEVS checked for every car, with the steps to get it home." },
];

async function Landing() {
  const examples = await getMatches();
  const hero = examples.find((c) => c.photo) ?? null;
  return (
    <div className="space-y-20">
      {/* The hero, Kensa-ya style: a real car blurred behind everything, the night-meet grid, a loud italic headline. */}
      <section className="relative isolate left-1/2 w-screen -translate-x-1/2 overflow-hidden pt-16 pb-14 lg:pt-20">
        <HeroVideo />
        <div className="grid-bg absolute inset-0 -z-10 opacity-60" />
        <div className="mx-auto grid max-w-6xl items-center gap-12 px-4 sm:px-6 lg:grid-cols-[1.1fr_1fr]">
          <div>
            <span className="inline-flex items-center gap-2 rounded-full border border-accent/50 bg-accent/10 px-4 py-1.5 text-[11px] font-semibold tracking-[0.2em] text-accent uppercase">
              <span className="size-1.5 rounded-full bg-accent shadow-[0_0_8px_var(--color-accent)]" /> Japanese auction watcher
            </span>
            <h1 className="display-xl mt-6">
              Find it
              <br />
              before
              <br />
              <span className="glow-red">anyone</span>
              <br />
              else.
            </h1>
            <p className="mt-6 max-w-lg text-lg text-muted">
              Tell Mitsuke the car you want and what you can spend on the ground. It watches every Japanese auction around the clock and pings your phone the
              moment one fits, with the landed cost and whether you can import it.
            </p>
            <div className="mt-8 flex flex-wrap items-center gap-3">
              <Link href="/login" className="btn-primary px-7 py-4 text-base">
                Start watching, free <ArrowRight className="size-4" aria-hidden />
              </Link>
              <Link href="/watchlists/new" className="btn-ghost px-6 py-4 text-base text-text">Build a watchlist</Link>
            </div>
            <dl className="mt-10 grid max-w-lg grid-cols-3 gap-6 border-t border-hairline pt-6 text-sm">
              <div><dt className="text-faint">Checks</dt><dd className="figure mt-1 text-sm sm:text-lg">Every 10 min</dd></div>
              <div><dt className="text-faint">Watches</dt><dd className="figure mt-1 text-sm sm:text-lg">USS, TAA +</dd></div>
              <div><dt className="text-faint">Lands in</dt><dd className="figure mt-1 text-sm sm:text-lg">AU · NZ · US</dd></div>
            </dl>
          </div>
          {hero && <HeroCard card={hero} />}
        </div>
      </section>

      <section className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {PROMISES.map(({ icon: Icon, title, body }) => (
          <div key={title} className="card p-5">
            <span className="grid size-10 place-items-center rounded-xl border border-accent/30 bg-accent/10 text-accent">
              <Icon className="size-5" aria-hidden />
            </span>
            <p className="mt-4 font-semibold">{title}</p>
            <p className="mt-1 text-sm text-muted">{body}</p>
          </div>
        ))}
      </section>

      <section>
        <div className="flex items-end justify-between gap-4">
          <div>
            <p className="label">Live from the auctions</p>
            <h2 className="display mt-2 text-3xl">Found this week</h2>
          </div>
          <Watchlists />
        </div>
        {examples.length === 0 ? (
          <p className="card mt-6 p-8 text-center text-muted">No matches yet. Mitsuke checks the auctions every ten minutes.</p>
        ) : (
          <Grid cards={examples} />
        )}
      </section>
    </div>
  );
}

/** The landing hero: a real lot, framed like a magazine cover. */
function HeroCard({ card: c }: { card: LotCard }) {
  return (
    <Link href={`/lot/${c.id}`} className="card underglow group relative block overflow-hidden border-accent/30">
      {/* eslint-disable-next-line @next/next/no-img-element -- remote auction photos via signed URLs */}
      <img src={c.photo!} alt={c.title} className="aspect-[4/3] w-full object-cover transition duration-500 group-hover:scale-[1.02]" />
      <div className="absolute inset-0 bg-gradient-to-t from-black/90 via-black/20 to-transparent" />
      <div className="absolute inset-x-0 bottom-0 p-6">
        <p className="label text-white/70">Found this week</p>
        <p className="mt-2 text-xl font-bold">{c.title}</p>
        <div className="mt-3 flex items-end justify-between gap-4">
          <div>
            <p className="text-xs text-white/60">Est. landed</p>
            <p className="figure text-4xl">{money(c.landedTotal)}</p>
          </div>
          {c.dealScore != null && <DealBadge score={c.dealScore} label={c.dealLabel} />}
        </div>
      </div>
    </Link>
  );
}

async function Watchlists() {
  const lists = await getWatchlists();
  if (lists.length === 0) return null;
  return (
    <p className="hidden text-sm text-faint sm:block">
      Watching for {lists.map(({ watchlist: w }) => w.name).slice(0, 3).join(", ")}
      {lists.length > 3 ? ` and ${lists.length - 3} more` : ""}
    </p>
  );
}

function Grid({ cards }: { cards: LotCard[] }) {
  return (
    <div className="mt-6 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
      {cards.map((c) => <Card key={`${c.id}-${c.watchlistName}`} card={c} />)}
    </div>
  );
}

function DealBadge({ score, label }: { score: number; label: string | null }) {
  const great = score >= 80;
  return (
    <span className={`tabular inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-semibold backdrop-blur ${great ? "bg-accent text-white" : "bg-black/60 text-white/90"}`}>
      <Gauge className="size-3.5" aria-hidden />
      {score}/100{label && great ? ` · ${label}` : ""}
    </span>
  );
}

function Card({ card: c }: { card: LotCard }) {
  return (
    <Link href={`/lot/${c.id}`} className="card group overflow-hidden transition duration-200 hover:-translate-y-1 hover:border-white/15">
      <div className="relative bg-black">
        {c.photo ? (
          // eslint-disable-next-line @next/next/no-img-element -- remote auction photos via redirecting, signed URLs
          <img src={c.photo} alt={c.title} className="aspect-[16/10] w-full object-cover opacity-90 transition duration-300 group-hover:opacity-100" loading="lazy" />
        ) : (
          <div className="aspect-[16/10]" />
        )}
        <div className="absolute inset-0 bg-gradient-to-t from-black/70 to-transparent" />
        <div className="absolute top-3 right-3">{c.dealScore != null && <DealBadge score={c.dealScore} label={c.dealLabel} />}</div>
        {c.highFlags > 0 && (
          <span className="absolute top-3 left-3 inline-flex items-center gap-1.5 rounded-full bg-black/70 px-3 py-1 text-xs font-semibold text-accent backdrop-blur">
            <TriangleAlert className="size-3.5" aria-hidden /> Check sheet
          </span>
        )}
        <p className="absolute bottom-3 left-4 right-4 truncate text-lg font-semibold">{c.title}</p>
      </div>
      <div className="p-5">
        <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-muted">
          <span>{km(c.mileageKm)}</span>
          <span className="text-faint">·</span>
          <span>Grade {c.grade ?? "—"}</span>
          {c.auctionDay && (
            <>
              <span className="text-faint">·</span>
              <span className="inline-flex items-center gap-1"><CalendarDays className="size-3.5" aria-hidden />{day(c.auctionDay)}</span>
            </>
          )}
        </p>
        <div className="mt-4 flex items-end justify-between border-t border-hairline pt-4">
          <div>
            <p className="label">Est. landed</p>
            <p className="figure mt-1 text-2xl">{money(c.landedTotal)}</p>
          </div>
          <p className="tabular text-sm text-muted">{money(c.openingBid, { compact: true })} opening</p>
        </div>
      </div>
    </Link>
  );
}

function GridSkeleton() {
  return (
    <div className="grid animate-pulse gap-5 pt-10 sm:grid-cols-2 lg:grid-cols-3">
      {[0, 1, 2].map((i) => <div key={i} className="card h-80" />)}
    </div>
  );
}
