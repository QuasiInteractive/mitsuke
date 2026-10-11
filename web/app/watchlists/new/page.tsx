import type { Metadata } from "next";
import { createWatchlist } from "@/app/actions";
import { WatchlistForm } from "../WatchlistForm";

export const metadata: Metadata = { title: "New watchlist" };

export default function NewWatchlistPage() {
  return (
    <div className="mx-auto max-w-3xl">
      <section className="relative isolate left-1/2 w-screen -translate-x-1/2 overflow-hidden pt-12 pb-2">
        <div className="grid-bg absolute inset-0 -z-10 opacity-70" />
        <div className="mx-auto max-w-3xl px-4 sm:px-6">
          <p className="label text-accent">New watchlist</p>
          <h1 className="display mt-3">
            What are you <span className="glow-red">hunting for?</span>
          </h1>
          <p className="mt-3 max-w-xl text-muted">Pick a classic or describe your own. Mitsuke alerts you when one turns up at auction.</p>
        </div>
      </section>
      <WatchlistForm action={createWatchlist} />
    </div>
  );
}
