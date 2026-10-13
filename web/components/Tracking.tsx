import Link from "next/link";
import { CircleCheck, CircleDashed, EyeOff, Radar, Repeat } from "lucide-react";
import type { LotTracking } from "@/lib/api";
import { day, money, shortDate } from "@/lib/format";

const STATUS = {
  Live: { icon: Radar, tone: "text-good", label: "Tracking live" },
  Waiting: { icon: CircleDashed, tone: "text-amber", label: "Auction over" },
  Relisted: { icon: Repeat, tone: "text-cyan", label: "Back at auction" },
  ProbablySold: { icon: CircleCheck, tone: "text-muted", label: "Probably sold" },
  CantFollow: { icon: EyeOff, tone: "text-faint", label: "Can't follow" },
} as const;

/** What Mitsuke has seen of this car: opening-bid changes, and whether it came back to a later auction. */
export function Tracking({ tracking: t }: { tracking: LotTracking }) {
  const s = STATUS[t.status];
  const Icon = s.icon;
  return (
    <section className="card p-5">
      <div className="flex items-center justify-between gap-3">
        <h2 className="flex items-center gap-3 text-lg font-semibold">
          <span className="grid size-9 place-items-center rounded-xl border border-hairline bg-black/30 text-muted">
            <Icon className={`size-[18px] ${s.tone}`} aria-hidden />
          </span>
          Tracking this car
        </h2>
        <span className={`inline-flex items-center gap-1.5 text-xs font-semibold ${s.tone}`}>
          {t.status === "Live" && <span className="size-1.5 animate-pulse rounded-full bg-good" />}
          {s.label}
        </span>
      </div>
      <p className="mt-3 font-medium">{t.headline}</p>
      {t.detail && <p className="mt-1 text-sm text-muted">{t.detail}</p>}

      {t.later.length > 0 && (
        <ul className="mt-4 space-y-2">
          {t.later.map((l) => (
            <li key={l.listingId}>
              <Link href={`/lot/${l.listingId}`} className="flex items-center justify-between gap-3 rounded-xl border border-hairline bg-black/25 px-3.5 py-2.5 text-sm hover:border-white/20">
                <span>{l.auctionHouse ?? "Auction"}{l.auctionDay && <span className="text-faint"> · {day(l.auctionDay)}</span>}</span>
                <span className="tabular">{l.openingBid ? money(l.openingBid) : "no price yet"} →</span>
              </Link>
            </li>
          ))}
        </ul>
      )}

      <dl className="mt-4 grid grid-cols-3 gap-3 border-t border-hairline pt-3 text-xs">
        <div><dt className="text-faint">First seen</dt><dd className="mt-0.5">{shortDate(t.firstSeenAt)}</dd></div>
        <div><dt className="text-faint">Last checked</dt><dd className="mt-0.5">{shortDate(t.lastSeenAt)}</dd></div>
        <div>
          <dt className="text-faint">Opening bid</dt>
          <dd className={`mt-0.5 ${t.priceChangePercent && t.priceChangePercent < 0 ? "text-good" : t.priceChangePercent ? "text-amber" : ""}`}>
            {t.priceChanges === 0 ? "unchanged" : `${t.priceChangePercent! > 0 ? "+" : ""}${t.priceChangePercent}% (${t.priceChanges} change${t.priceChanges === 1 ? "" : "s"})`}
          </dd>
        </div>
      </dl>
    </section>
  );
}
