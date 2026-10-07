import type { Metadata } from "next";
import { NewWatchlistForm } from "./NewWatchlistForm";

export const metadata: Metadata = { title: "New watchlist" };

export default function NewWatchlistPage() {
  return (
    <div className="mx-auto max-w-2xl pt-4">
      <h1 className="text-3xl font-bold tracking-tight">What are you hunting for?</h1>
      <p className="mt-1 text-muted">Pick a classic or describe your own. Mitsuke alerts you when one turns up at auction.</p>
      <NewWatchlistForm />
    </div>
  );
}
