"use client";

import { useState, useSyncExternalStore, useTransition } from "react";
import Link from "next/link";
import { Gavel, Heart, X, ChevronRight } from "lucide-react";
import { setFeedback } from "@/app/actions";

type Feedback = "Watching" | "NotForMe" | null;

/**
 * Whether a partner exporter is signed up to take requests. Until then the bar says so plainly: a request records
 * interest and Nick follows up by hand. Set NEXT_PUBLIC_BID_PARTNER_LIVE=true once an exporter is live.
 */
const PARTNER_LIVE = process.env.NEXT_PUBLIC_BID_PARTNER_LIVE === "true";

// Bids must reach the exporter before the auction day starts (the end of the day minus 24 hours).
const subscribeMinute = (tick: () => void) => {
  const t = setInterval(tick, 60_000);
  return () => clearInterval(t);
};
const nowMinute = () => Math.floor(Date.now() / 60_000) * 60_000;

type Status = { kind: "idle" } | { kind: "sending" } | { kind: "sent" } | { kind: "error"; message: string };

/**
 * The sticky action bar. "I want to bid" asks for a max bid and contact details and passes them to a partner
 * exporter (Mitsuke never bids or takes money). Keep watching / Not for me arrive with accounts.
 */
export function BidBar({
  lotId,
  title,
  openingBidJpy,
  signedIn,
  feedback,
  auctionEndsAt,
}: {
  lotId: string;
  title: string;
  openingBidJpy: number | null;
  signedIn: boolean;
  feedback: Feedback;
  auctionEndsAt: string | null;
}) {
  const now = useSyncExternalStore(subscribeMinute, nowMinute, () => null);
  const closed = now !== null && auctionEndsAt !== null && new Date(auctionEndsAt).getTime() - 86_400_000 <= now;
  const [open, setOpen] = useState(false);
  const [saving, startSaving] = useTransition();
  const toggle = (kind: Exclude<Feedback, null>) => startSaving(() => setFeedback(lotId, feedback === kind ? null : kind));
  const [status, setStatus] = useState<Status>({ kind: "idle" });

  async function submit(form: FormData) {
    setStatus({ kind: "sending" });
    const res = await fetch(`/api/bid/${lotId}`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        maxBidJpy: Number(String(form.get("maxBid") ?? "").replace(/[^\d]/g, "")),
        name: form.get("name"),
        email: form.get("email"),
        note: form.get("note") || null,
      }),
    });
    if (res.ok) return setStatus({ kind: "sent" });
    const body = await res.json().catch(() => null);
    const first = body?.errors ? Object.values(body.errors as Record<string, string[]>)[0]?.[0] : null;
    setStatus({ kind: "error", message: first ?? (res.status === 429 ? "Too many requests. Try again in a few minutes." : "That didn't go through. Try again.") });
  }

  return (
    <>
      <div className="fixed inset-x-0 bottom-0 z-20 border-t border-hairline bg-ink/80 backdrop-blur-xl">
        <div className="mx-auto flex max-w-6xl items-center gap-2 px-4 py-3 sm:px-6">
          <button
            onClick={() => setOpen(true)}
            disabled={closed}
            className="btn-primary flex-1 px-5 py-3.5 text-base whitespace-nowrap disabled:cursor-not-allowed disabled:opacity-40 disabled:grayscale sm:flex-none sm:px-9"
          >
            <Gavel className="size-5 shrink-0" aria-hidden />
            {closed ? "Bidding closed" : PARTNER_LIVE ? "I want to bid" : "Request a bid"}
            <ChevronRight className="hidden size-4 opacity-80 sm:block" aria-hidden />
          </button>
          {signedIn ? (
            <>
              <button
                disabled={saving}
                onClick={() => toggle("Watching")}
                className={`inline-flex items-center gap-2 rounded-2xl border px-4 py-3.5 text-sm transition ${feedback === "Watching" ? "border-accent bg-accent/15 text-text" : "border-hairline text-muted hover:border-white/20 hover:text-text"}`}
              >
                <Heart className={`size-4 ${feedback === "Watching" ? "fill-accent text-accent" : ""}`} aria-hidden />
                {feedback === "Watching" ? "Watching" : "Keep watching"}
              </button>
              <button
                disabled={saving}
                onClick={() => toggle("NotForMe")}
                className={`hidden items-center gap-2 rounded-2xl border px-4 py-3.5 text-sm transition sm:inline-flex ${feedback === "NotForMe" ? "border-hairline bg-white/5 text-text" : "border-hairline text-muted hover:border-white/20 hover:text-text"}`}
              >
                <X className="size-4" aria-hidden />
                {feedback === "NotForMe" ? "Hidden · undo" : "Not for me"}
              </button>
            </>
          ) : (
            <Link href="/login" className="rounded-2xl border border-line px-4 py-3.5 text-sm text-muted hover:text-text">♡ Sign in to watch</Link>
          )}
        </div>
      </div>

      {open && (
        <div className="fixed inset-0 z-30 grid place-items-end bg-black/70 backdrop-blur-sm sm:place-items-center" onClick={() => setOpen(false)}>
          <div className="card w-full max-w-md rounded-b-none p-6 sm:rounded-b-[1.25rem]" onClick={(e) => e.stopPropagation()} role="dialog" aria-modal="true" aria-labelledby="bid-title">
            {status.kind === "sent" ? (
              <div className="py-6 text-center">
                <div className="mx-auto mb-3 grid size-12 place-items-center rounded-full bg-good/15 text-2xl text-good">✓</div>
                <h2 id="bid-title" className="text-lg font-semibold">{PARTNER_LIVE ? "Request sent" : "Request saved"}</h2>
                <p className="mt-2 text-sm text-muted">
                  {PARTNER_LIVE
                    ? "We'll pass it to our partner exporter and email you. Nothing is charged and no bid is placed until they confirm with you."
                    : "We'll email you if we can connect you with an exporter before bidding closes. Nothing is charged and no bid is placed."}
                </p>
                <button onClick={() => setOpen(false)} className="mt-6 rounded-xl border border-line px-5 py-2 text-sm">Close</button>
              </div>
            ) : (
              <form action={submit} className="space-y-4">
                <div>
                  <h2 id="bid-title" className="text-lg font-semibold">{PARTNER_LIVE ? "Bid on this car" : "Request a bid"}</h2>
                  <p className="mt-1 text-sm text-muted">{title}</p>
                </div>
                {!PARTNER_LIVE && (
                  <p className="rounded-xl border border-amber/40 bg-amber/10 p-3 text-sm text-amber">
                    We&apos;re still signing our exporter partner, so we can&apos;t promise a bid yet. Send your request and we&apos;ll do our best to connect you in time.
                  </p>
                )}
                <label className="block text-sm">
                  <span className="text-muted">Your maximum bid (yen)</span>
                  <input name="maxBid" inputMode="numeric" required placeholder={openingBidJpy ? Math.round(openingBidJpy).toLocaleString("en-AU") : "3,500,000"} className="tabular mt-1 w-full rounded-xl border border-line bg-ink px-3 py-2.5 text-lg outline-none focus:border-accent" />
                </label>
                <div className="grid gap-3 sm:grid-cols-2">
                  <label className="block text-sm">
                    <span className="text-muted">Name</span>
                    <input name="name" required maxLength={100} autoComplete="name" className="mt-1 w-full rounded-xl border border-line bg-ink px-3 py-2.5 outline-none focus:border-accent" />
                  </label>
                  <label className="block text-sm">
                    <span className="text-muted">Email</span>
                    <input name="email" type="email" required autoComplete="email" className="mt-1 w-full rounded-xl border border-line bg-ink px-3 py-2.5 outline-none focus:border-accent" />
                  </label>
                </div>
                <label className="block text-sm">
                  <span className="text-muted">Anything we should know? (optional)</span>
                  <textarea name="note" maxLength={1000} rows={2} className="mt-1 w-full rounded-xl border border-line bg-ink px-3 py-2.5 outline-none focus:border-accent" />
                </label>
                <p className="text-xs text-faint">Mitsuke never bids or takes payment. A licensed partner exporter places the bid and handles your deposit directly.</p>
                {status.kind === "error" && <p className="text-sm text-accent">{status.message}</p>}
                <div className="flex gap-2">
                  <button type="button" onClick={() => setOpen(false)} className="rounded-xl border border-line px-4 py-3 text-sm">Cancel</button>
                  <button disabled={status.kind === "sending"} className="flex-1 rounded-xl bg-accent py-3 font-semibold hover:bg-accent-strong disabled:opacity-60">
                    {status.kind === "sending" ? "Sending…" : "Send bid request"}
                  </button>
                </div>
              </form>
            )}
          </div>
        </div>
      )}
    </>
  );
}
