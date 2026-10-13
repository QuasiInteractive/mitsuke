"use client";

import { useEffect, useState } from "react";
import { Calculator } from "lucide-react";
import type { MaxBidResult, Money } from "@/lib/api";
import { money } from "@/lib/format";

const SYMBOL: Record<string, string> = { AU: "A$", NZ: "NZ$", US: "US$" };
const TONE = {
  Room: "text-good",
  Tight: "text-amber",
  BelowOpening: "text-accent",
} as const;

/**
 * "What's the most I can bid?": the user's budget on the ground, the landed-cost engine run backwards on the API,
 * answered as they type. Starts a little above what the car costs landed at its opening bid.
 */
export function MaxBid({ lotId, destination, landedAtOpening, openingLow, openingHigh }: {
  lotId: string;
  destination: string;
  landedAtOpening: Money | null;
  openingLow: Money | null;
  openingHigh: Money | null;
}) {
  const start = landedAtOpening ? Math.ceil((landedAtOpening.amount + 5_000) / 5_000) * 5_000 : 45_000;
  const [budget, setBudget] = useState(String(start));
  const [result, setResult] = useState<MaxBidResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const amount = Number(budget);
    if (!amount) return;
    const ctl = new AbortController();
    const t = setTimeout(async () => {
      try {
        const res = await fetch(`/api/max-bid?${new URLSearchParams({ id: lotId, budget: String(amount), to: destination })}`, { signal: ctl.signal });
        if (!res.ok) throw new Error();
        setResult((await res.json()) as MaxBidResult);
        setError(null);
      } catch (e) {
        if (!ctl.signal.aborted) setError("Couldn't work that out right now. Try again in a moment.");
        void e;
      }
    }, 350);
    return () => {
      clearTimeout(t);
      ctl.abort();
    };
  }, [budget, lotId, destination]);

  const symbol = SYMBOL[destination] ?? "A$";
  return (
    <section className="card p-5">
      <div className="flex items-center justify-between gap-3">
        <h2 className="flex items-center gap-3 text-lg font-semibold">
          <span className="grid size-9 place-items-center rounded-xl border border-hairline bg-black/30 text-muted">
            <Calculator className="size-[18px]" aria-hidden />
          </span>
          Max bid calculator
        </h2>
        <span className="text-xs text-faint">landed in {destination}</span>
      </div>

      <label className="mt-4 block">
        <span className="label">Your budget, on the road</span>
        <div className="relative">
          <span className="pointer-events-none absolute top-1/2 left-4 mt-1 -translate-y-1/2 text-xl font-semibold text-muted">{symbol}</span>
          <input
            className="field tabular py-3.5 pl-14 text-2xl font-bold tracking-tight"
            inputMode="numeric"
            value={budget ? Number(budget).toLocaleString("en-AU") : ""}
            onChange={(e) => setBudget(e.target.value.replace(/[^\d]/g, ""))}
            aria-describedby="maxbid-result"
          />
        </div>
      </label>

      <div id="maxbid-result" aria-live="polite" className="mt-5">
        {error && <p className="text-sm text-accent">{error}</p>}
        {result && !error && (
          <>
            <div className="flex flex-wrap items-end justify-between gap-4">
              <div>
                <p className="label">Bid at most</p>
                <p className={`figure mt-1 text-5xl ${TONE[result.room]}`}>{result.maxBid ? money(result.maxBid) : "—"}</p>
              </div>
              {result.headroomPercent != null && (
                <div className="text-right">
                  <p className="label">Over the opening bid</p>
                  <p className={`figure mt-1 text-2xl ${TONE[result.room]}`}>{result.headroomPercent > 0 ? "+" : ""}{result.headroomPercent}%</p>
                </div>
              )}
            </div>
            {result.landedAtMax && (
              <p className="tabular mt-2 text-xs text-faint">
                Lands at about {money(result.landedAtMax.total)} (range {money(result.landedAtMax.low)}–{money(result.landedAtMax.high).replace(/^[^\d]+/, "")})
              </p>
            )}
            <p className="mt-3 text-sm text-muted">{result.summary}</p>
            <Ladder opening={result.openingBid} max={result.maxBid} low={openingLow} high={openingHigh} />
          </>
        )}
      </div>
      <p className="mt-4 border-t border-hairline pt-3 text-xs text-faint">
        Uses the same estimate as the landed cost above, so the exporter&apos;s fee and on-road costs in your state come on top. Live bidding isn&apos;t
        published, so no one can tell you your odds: this tells you your limit.
      </p>
    </section>
  );
}

/** Opening bid, your max and where similar cars opened, on one line: is your max anywhere near the field? */
function Ladder({ opening, max, low, high }: { opening: Money | null; max: Money | null; low: Money | null; high: Money | null }) {
  const points = [opening?.amount, max?.amount, low?.amount, high?.amount].filter((v): v is number => v != null);
  if (!opening || !max || points.length < 3) return null;
  const lo = Math.min(...points) * 0.95;
  const hi = Math.max(...points) * 1.05;
  const pos = (v: number) => `${((v - lo) / (hi - lo)) * 100}%`;
  const yen = (v: number) => money({ amount: v, currency: "JPY" }, { compact: true });
  return (
    <div className="mt-5">
      <div className="relative h-2 rounded-full bg-white/[0.06]">
        {low && high && <div className="absolute inset-y-0 rounded-full bg-cyan/25" style={{ left: pos(low.amount), right: `calc(100% - ${pos(high.amount)})` }} />}
        <span className="absolute top-1/2 h-4 w-0.5 -translate-y-1/2 bg-text" style={{ left: pos(opening.amount) }} />
        <span className="absolute top-1/2 size-3.5 -translate-x-1/2 -translate-y-1/2 rounded-full bg-good shadow-[0_0_10px_var(--color-good)]" style={{ left: pos(max.amount) }} />
      </div>
      <div className="tabular mt-2 flex flex-wrap justify-between gap-x-4 text-[11px] text-faint">
        <span><span className="text-text">|</span> opens {yen(opening.amount)}</span>
        <span><span className="text-good">●</span> your max {yen(max.amount)}</span>
        {low && high && <span><span className="text-cyan">▬</span> similar cars opened {yen(low.amount)}–{yen(high.amount).replace("¥", "")}</span>}
      </div>
    </div>
  );
}
