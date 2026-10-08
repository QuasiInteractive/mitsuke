import type { Metadata } from "next";
import { createWatchlist } from "@/app/actions";
import { WatchlistForm } from "../WatchlistForm";

export const metadata: Metadata = { title: "New watchlist" };

export default function NewWatchlistPage() {
  return (
    <div className="mx-auto max-w-3xl pt-8">
      <p className="label text-accent">New watchlist</p>
      <h1 className="display mt-3">What are you hunting for?</h1>
      <p className="mt-3 max-w-xl text-muted">Pick a classic or describe your own. Mitsuke alerts you when one turns up at auction.</p>
      <WatchlistForm action={createWatchlist} />
    </div>
  );
}
