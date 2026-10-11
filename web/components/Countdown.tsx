"use client";

import { useSyncExternalStore } from "react";

const TICK_MS = 30_000;

// A shared clock: re-renders every 30 s, and stays null during server rendering so the markup never mismatches.
const subscribe = (onTick: () => void) => {
  const timer = setInterval(onTick, TICK_MS);
  return () => clearInterval(timer);
};
const snapshot = () => Math.floor(Date.now() / TICK_MS) * TICK_MS;

/**
 * Counts down to the start of the auction day in Japan. Sources give the end of the day (midnight JST),
 * not a hammer time, and bids must be in before the day starts, so that's the honest deadline.
 */
export function Countdown({ auctionEndsAt }: { auctionEndsAt: string }) {
  const now = useSyncExternalStore(subscribe, snapshot, () => null);
  const deadline = new Date(auctionEndsAt).getTime() - 24 * 60 * 60 * 1000;

  if (now === null) return <span className="text-muted">…</span>;
  const ms = deadline - now;
  if (ms <= 0) return <span className="font-semibold text-accent">Auction day has started</span>;

  const d = Math.floor(ms / 86_400_000);
  const h = Math.floor((ms % 86_400_000) / 3_600_000);
  const m = Math.floor((ms % 3_600_000) / 60_000);
  return (
    <span className="tabular text-3xl font-bold tracking-tight">
      {d > 0 && <>{d}<span className="mr-2 text-lg text-muted">d</span></>}
      {h}<span className="mr-2 text-lg text-muted">h</span>
      {m}<span className="text-lg text-muted">m</span>
    </span>
  );
}
