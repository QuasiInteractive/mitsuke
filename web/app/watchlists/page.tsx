import { Suspense } from "react";
import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";
import { getUser } from "@/lib/supabase/server";
import { getMyCloseMatches, getMyWatchlists, type MyWatchlist } from "@/lib/api";
import { day, km, money } from "@/lib/format";
import { deleteWatchlist, setWatchlistActive } from "@/app/actions";
import { PushToggle } from "@/components/PushToggle";

export const metadata: Metadata = { title: "Your watchlists" };

export default function WatchlistsPage() {
  return (
    <div className="space-y-8 pt-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <p className="label text-accent">Watchlists</p>
          <h1 className="display mt-3">The hunt</h1>
          <p className="mt-3 max-w-xl text-muted">Mitsuke checks the Japanese auctions for each one every ten minutes, and shows what&apos;s close when nothing fits yet.</p>
        </div>
        <Link href="/watchlists/new" className="btn-primary shrink-0 px-6 py-3.5">+ New watchlist</Link>
      </div>
      <PushToggle />
      <Suspense fallback={<div className="card h-48 animate-pulse" />}>
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
      <div className="card p-10 text-center">
        <p className="text-xl font-semibold">No watchlists yet</p>
        <p className="mt-2 text-muted">Tell Mitsuke the car you want and your budget, landed.</p>
        <Link href="/watchlists/new" className="btn-primary mt-6 px-6 py-3">Create your first watchlist</Link>
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
  const budget = w.maxPrice ? `opening bid under ${money(w.maxPrice)}` : w.maxLanded ? `under ${money(w.maxLanded)} landed in ${w.destination}` : null;
  const facts = [
    w.modelCodes.length > 0 && w.modelCodes.join(", "),
    (w.yearFrom || w.yearTo) && `${w.yearFrom ?? "…"}–${w.yearTo ?? "…"}`,
    w.maxMileageKm && `under ${km(w.maxMileageKm)}`,
    w.minGrade && `grade ${w.minGrade}+`,
    budget,
  ].filter(Boolean);

  return (
    <section className={`card p-6 ${w.isActive ? "" : "opacity-60"}`}>
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-3">
            <h2 className="text-xl font-semibold tracking-tight">{w.name}</h2>
            <span className={`rounded-full px-2.5 py-0.5 text-xs font-semibold ${w.isActive ? "bg-good/15 text-good" : "bg-white/5 text-muted"}`}>
              {w.isActive ? "Watching" : "Paused"}
            </span>
          </div>
          <p className="mt-1 text-sm text-muted">{w.make} {w.model}</p>
          <p className="mt-3 text-sm text-faint">{facts.join(" · ")}</p>
        </div>
        <div className="text-right">
          <p className="tabular text-3xl font-bold">{item.matchCount}</p>
          <p className="label">match{item.matchCount === 1 ? "" : "es"}</p>
        </div>
      </div>

      {w.isActive && (
        <Suspense fallback={<div className="mt-5 h-24 animate-pulse rounded-2xl bg-white/[0.03]" />}>
          <CloseMatches id={w.id} token={token} />
        </Suspense>
      )}

      <div className="mt-5 flex flex-wrap gap-2 border-t border-hairline pt-4">
        <Link href={`/watchlists/${w.id}/edit`} className="btn-ghost">Edit</Link>
        <form action={setWatchlistActive.bind(null, w.id, !w.isActive)}>
          <button className="btn-ghost">{w.isActive ? "Pause" : "Resume"}</button>
        </form>
        <form action={deleteWatchlist.bind(null, w.id)}>
          <button className="btn-ghost hover:!border-accent/60 hover:!text-accent">Delete</button>
        </form>
      </div>
    </section>
  );
}

/** Cars at auction now that nearly fit, and what they miss by: so a quiet watchlist still shows the market. */
async function CloseMatches({ id, token }: { id: string; token: string }) {
  const close = await getMyCloseMatches(token, id);
  if (close.length === 0) {
    return <p className="mt-5 rounded-2xl border border-hairline bg-black/20 px-4 py-3 text-sm text-faint">Nothing close at auction right now. New lots land every day.</p>;
  }
  return (
    <div className="mt-5">
      <p className="label">Close, but not quite · {close.length} at auction now</p>
      <div className="no-scrollbar -mx-1 mt-3 flex gap-3 overflow-x-auto px-1 pb-1">
        {close.map(({ lot, missesBy }) => (
          <Link key={lot.id} href={`/lot/${lot.id}`} className="group w-64 shrink-0 overflow-hidden rounded-2xl border border-hairline bg-black/25 transition hover:border-white/20">
            <div className="aspect-[16/10] bg-raised">
              {lot.photo && (
                // eslint-disable-next-line @next/next/no-img-element -- auction photos come from the provider's CDN
                <img src={lot.photo} alt="" className="size-full object-cover opacity-90 transition group-hover:opacity-100" loading="lazy" />
              )}
            </div>
            <div className="p-3">
              <p className="truncate text-sm font-semibold">{lot.title}</p>
              <p className="mt-0.5 text-xs text-faint">
                {[lot.mileageKm != null && km(lot.mileageKm), lot.grade && `grade ${lot.grade}`, lot.auctionDay && day(lot.auctionDay)].filter(Boolean).join(" · ")}
              </p>
              {lot.landedTotal && <p className="tabular mt-2 text-lg font-bold">{money(lot.landedTotal)} <span className="text-xs font-normal text-faint">landed</span></p>}
              <ul className="mt-2 space-y-1">
                {missesBy.map((m) => (
                  <li key={m} className="flex gap-1.5 text-xs text-amber"><span aria-hidden>•</span>{m}</li>
                ))}
              </ul>
            </div>
          </Link>
        ))}
      </div>
    </div>
  );
}
