import { Suspense } from "react";
import type { Metadata } from "next";
import { notFound, redirect } from "next/navigation";
import { getUser } from "@/lib/supabase/server";
import { getMyWatchlists, type MyWatchlist } from "@/lib/api";
import { updateWatchlist } from "@/app/actions";
import { WatchlistForm, type WatchlistInitial } from "../../WatchlistForm";

export const metadata: Metadata = { title: "Edit watchlist" };

export default function EditWatchlistPage({ params }: PageProps<"/watchlists/[id]/edit">) {
  return (
    <div className="mx-auto max-w-2xl pt-4">
      <h1 className="text-3xl font-bold tracking-tight">Edit watchlist</h1>
      <p className="mt-1 text-muted">Changes apply from the next check. Cars you&apos;ve already been alerted about won&apos;t be sent again.</p>
      <Suspense fallback={<div className="mt-6 h-96 animate-pulse rounded-[1.25rem] bg-raised" />}>
        {params.then(({ id }) => <Editor id={id} />)}
      </Suspense>
    </div>
  );
}

async function Editor({ id }: { id: string }) {
  const user = await getUser();
  if (!user) redirect("/login");
  const item = (await getMyWatchlists(user.accessToken)).find((w) => w.watchlist.id === id);
  if (!item) notFound();
  return <WatchlistForm action={updateWatchlist.bind(null, id)} initial={toInitial(item)} submitLabel="Save changes" />;
}

function toInitial({ watchlist: w }: MyWatchlist): WatchlistInitial {
  const text = (n: number | null) => (n === null ? "" : String(n));
  return {
    name: w.name,
    make: w.make,
    model: w.model,
    modelCodes: w.modelCodes.join(", "),
    yearFrom: text(w.yearFrom),
    yearTo: text(w.yearTo),
    maxLandedAmount: text(w.maxLanded?.amount ?? null),
    destination: w.destination === "NZ" ? "NZ" : "AU",
    maxMileageKm: text(w.maxMileageKm),
    minGrade: text(w.minGrade),
    includeRepaired: w.includeRepaired,
  };
}
