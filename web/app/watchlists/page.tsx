import { Suspense } from "react";
import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";
import { getUser } from "@/lib/supabase/server";
import { getMyWatchlists, type MyWatchlist } from "@/lib/api";
import { km, money } from "@/lib/format";
import { deleteWatchlist, setWatchlistActive } from "@/app/actions";
import { PushToggle } from "@/components/PushToggle";

export const metadata: Metadata = { title: "Your watchlists" };

export default function WatchlistsPage() {
  return (
    <div className="space-y-6 pt-4">
      <div className="flex items-end justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">Your watchlists</h1>
          <p className="mt-1 text-muted">Mitsuke checks the Japanese auctions for each one every ten minutes.</p>
        </div>
        <Link href="/watchlists/new" className="shrink-0 rounded-2xl bg-accent px-5 py-3 font-semibold hover:bg-accent-strong">+ New</Link>
      </div>
      <PushToggle />
      <Suspense fallback={<div className="h-40 animate-pulse rounded-[1.25rem] bg-raised" />}>
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
        <p className="text-lg font-semibold">No watchlists yet</p>
        <p className="mt-1 text-muted">Tell Mitsuke the car you want and your budget, landed.</p>
        <Link href="/watchlists/new" className="mt-6 inline-block rounded-xl bg-accent px-5 py-2.5 font-semibold">Create your first watchlist</Link>
      </div>
    );
  }
  return <div className="grid gap-4 sm:grid-cols-2">{lists.map((w) => <Row key={w.watchlist.id} item={w} />)}</div>;
}

function Row({ item }: { item: MyWatchlist }) {
  const w = item.watchlist;
  const facts = [
    w.modelCodes.length > 0 && w.modelCodes.join(", "),
    (w.yearFrom || w.yearTo) && `${w.yearFrom ?? "…"}–${w.yearTo ?? "…"}`,
    w.maxMileageKm && `under ${km(w.maxMileageKm)}`,
    w.minGrade && `grade ${w.minGrade}+`,
    w.maxLanded && `under ${money(w.maxLanded)} landed in ${w.destination}`,
  ].filter(Boolean);

  return (
    <div className={`card p-5 ${w.isActive ? "" : "opacity-60"}`}>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-lg font-semibold">{w.name}</p>
          <p className="text-sm text-muted">{w.make} {w.model}</p>
        </div>
        <span className={`rounded-full px-2.5 py-1 text-xs font-semibold ${w.isActive ? "bg-good/15 text-good" : "bg-raised text-muted"}`}>
          {w.isActive ? "Watching" : "Paused"}
        </span>
      </div>
      <p className="mt-3 text-sm text-muted">{facts.join(" · ")}</p>
      <p className="mt-3 text-sm">
        <span className="font-semibold">{item.matchCount}</span> <span className="text-muted">match{item.matchCount === 1 ? "" : "es"} so far</span>
      </p>
      <div className="mt-4 flex gap-2">
        <form action={setWatchlistActive.bind(null, w.id, !w.isActive)}>
          <button className="rounded-xl border border-line px-3 py-2 text-sm hover:border-text">{w.isActive ? "Pause" : "Resume"}</button>
        </form>
        <Link href={`/watchlists/${w.id}/edit`} className="rounded-xl border border-line px-3 py-2 text-sm hover:border-text">Edit</Link>
        <form action={deleteWatchlist.bind(null, w.id)}>
          <button className="rounded-xl border border-line px-3 py-2 text-sm text-muted hover:border-accent hover:text-accent">Delete</button>
        </form>
      </div>
    </div>
  );
}
