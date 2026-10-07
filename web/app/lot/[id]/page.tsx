import { Suspense } from "react";
import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { getLot, getMyFeedback, type LotView } from "@/lib/api";
import { getUser } from "@/lib/supabase/server";
import { day, km, money, shortDate } from "@/lib/format";
import { Gallery } from "@/components/Gallery";
import { Countdown } from "@/components/Countdown";
import { CarDiagram, SEVERITY_COLOUR } from "@/components/CarDiagram";
import { PriceChart, type ChartPoint } from "@/components/PriceChart";
import { BidBar } from "@/components/BidBar";
import { damageSeverity } from "@/lib/format";

export const metadata: Metadata = { title: "Lot" };

// The shell renders instantly; the live lot (prices, countdown, sheet) streams in behind the skeleton.
export default function LotPage({ params }: PageProps<"/lot/[id]">) {
  return (
    <Suspense fallback={<LotSkeleton />}>
      {params.then(({ id }) => (
        <Lot id={id} />
      ))}
    </Suspense>
  );
}

async function Lot({ id }: { id: string }) {
  const [lot, user] = await Promise.all([getLot(id), getUser()]);
  if (!lot) notFound();
  const feedback = user ? await getMyFeedback(user.accessToken, lot.id) : null;

  return (
    <>
      <div className="grid gap-6 lg:grid-cols-[1.15fr_1fr] lg:items-start">
        <div className="space-y-4 lg:sticky lg:top-4">
          <Gallery photos={lot.photos} title={lot.title} />
          {lot.sheetImage && (
            <a href={lot.sheetImage} target="_blank" rel="noreferrer" className="card flex items-center justify-between px-4 py-3 text-sm hover:border-accent">
              <span>
                <span className="font-jp text-muted">出品票</span> Original auction sheet
              </span>
              <span className="text-muted">Open ↗</span>
            </a>
          )}
        </div>

        <div className="space-y-4">
          <Header lot={lot} />
          <PriceCards lot={lot} />
          {lot.sheet?.redFlags.some((f) => f.severity === "High") && <RedFlags lot={lot} />}
          {lot.auctionEndsAt && <AuctionCard lot={lot} />}
          {lot.sheet && <Condition lot={lot} />}
          {lot.landed && <CostBreakdown lot={lot} />}
          <History lot={lot} />
          <p className="px-1 text-center text-xs text-faint">
            {lot.attribution} · {lot.disclaimer}
          </p>
        </div>
      </div>
      <BidBar
        lotId={lot.id}
        title={lot.title}
        openingBidJpy={lot.openingBid?.currency === "JPY" ? lot.openingBid.amount : null}
        signedIn={user !== null}
        feedback={feedback?.kind ?? null}
      />
    </>
  );
}

function Header({ lot }: { lot: LotView }) {
  const chips = [
    km(lot.mileageKm),
    lot.grade && `Grade ${lot.grade}${lot.gradeIsRepaired ? " (repaired)" : ""}`,
    lot.transmission,
    lot.rightHandDrive == null ? null : lot.rightHandDrive ? "RHD" : "LHD",
    lot.isModified && "Modified",
    lot.sheet?.colour,
  ].filter(Boolean);
  return (
    <div>
      <h1 className="text-2xl leading-tight font-bold tracking-tight sm:text-3xl">{lot.title}</h1>
      <div className="mt-3 flex flex-wrap gap-2">
        {chips.map((c) => (
          <span key={String(c)} className="rounded-full border border-line bg-raised px-3 py-1 text-sm">{c}</span>
        ))}
      </div>
    </div>
  );
}

function PriceCards({ lot }: { lot: LotView }) {
  const deal = lot.deal;
  return (
    <div className="grid grid-cols-[1.5fr_1fr] gap-3">
      <div className="card relative overflow-hidden p-5">
        <div className="pointer-events-none absolute -top-10 -right-10 size-36 rounded-full bg-accent/20 blur-2xl" />
        <p className="text-sm text-muted">Est. landed in {lot.landed?.destination ?? "AU"}</p>
        <p className="tabular mt-1 text-4xl font-bold tracking-tight">{money(lot.landed?.total)}</p>
        {lot.landed && (
          <p className="tabular mt-1 text-xs text-faint">
            range {money(lot.landed.low)}–{money(lot.landed.high).replace(/^[A-Z$]+/, "")}
          </p>
        )}
        <p className="mt-3 text-sm text-muted">
          Opening bid <span className="tabular text-text">{money(lot.openingBid)}</span>
          {lot.auctionHouse && <> · {lot.auctionHouse}</>}
        </p>
      </div>
      <div className={`card p-5 ${deal?.score != null && deal.score >= 80 ? "card-alert" : ""}`}>
        <p className="text-sm text-muted">Deal score</p>
        {deal?.score != null ? (
          <>
            <p className="tabular mt-1 text-4xl font-bold">
              {deal.score}
              <span className="text-lg text-muted">/100</span>
            </p>
            <p className="mt-1 font-semibold text-accent">{deal.label}</p>
            <p className="mt-2 text-xs text-faint">
              vs {deal.comparableCount} cars{deal.confidence === "Low" ? " · low confidence" : ""}
            </p>
          </>
        ) : (
          <p className="mt-2 text-sm text-muted">{deal?.label ?? "Not scored"}</p>
        )}
      </div>
    </div>
  );
}

function RedFlags({ lot }: { lot: LotView }) {
  return (
    <div className="card card-alert p-5">
      {lot.sheet!.redFlags
        .filter((f) => f.severity === "High")
        .map((f) => (
          <div key={f.title} className="flex gap-3">
            <span className="text-xl text-accent">⚠</span>
            <div>
              <p className="font-semibold">{f.title}</p>
              <p className="mt-0.5 text-sm text-muted">{f.detail}</p>
            </div>
          </div>
        ))}
      <p className="mt-3 text-xs text-faint">A cheap price can have a reason. Read this before the deal score.</p>
    </div>
  );
}

function AuctionCard({ lot }: { lot: LotView }) {
  return (
    <div className="card flex items-center gap-4 px-5 py-4">
      <span className="grid size-10 place-items-center rounded-full border-2 border-accent text-accent">◷</span>
      <div className="flex-1">
        <p className="text-sm text-muted">Bids close before</p>
        <Countdown auctionEndsAt={lot.auctionEndsAt!} />
      </div>
      <p className="text-right text-sm text-muted">
        {day(lot.auctionDay)}, Japan
        {lot.lotNumber && <span className="block text-xs text-faint">Lot {lot.lotNumber}</span>}
      </p>
    </div>
  );
}

function Condition({ lot }: { lot: LotView }) {
  const sheet = lot.sheet!;
  return (
    <section className="card p-5">
      <div className="flex items-baseline justify-between">
        <h2 className="text-lg font-semibold">Condition</h2>
        <span className="text-xs text-faint">
          from the auction sheet{sheet.overallGrade && ` · grade ${sheet.overallGrade}`}
          {lot.interiorGrade && ` · interior ${lot.interiorGrade}`}
        </span>
      </div>
      <p className="mt-3 text-sm leading-relaxed text-muted">{sheet.summary}</p>
      <div className="mt-4 grid items-center gap-4 sm:grid-cols-[auto_1fr]">
        <div className="justify-self-center">
          <CarDiagram damage={sheet.damage} />
        </div>
        <ul className="space-y-2">
          {sheet.damage.map((d, i) => (
            <li key={i} className="flex items-start gap-3 rounded-xl border border-line bg-ink/60 px-3 py-2 text-sm">
              <span className="mt-1.5 size-2.5 shrink-0 rounded-full" style={{ background: SEVERITY_COLOUR[damageSeverity(d.code)] }} />
              <span>
                <span className="font-medium">{d.name ?? d.code}</span>
                {d.size && <span className="text-muted">, {d.size.split(" (")[0]}</span>}
                <span className="text-muted">, {d.location.toLowerCase()}</span>
                {!d.name && <span className="block text-xs text-faint">Code {d.code} as written on the sheet</span>}
              </span>
            </li>
          ))}
        </ul>
      </div>
      {sheet.watchOut.length > 0 && (
        <div className="mt-4">
          <h3 className="text-sm font-semibold">Check before bidding</h3>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-muted">
            {sheet.watchOut.map((w) => <li key={w}>{w}</li>)}
          </ul>
        </div>
      )}
      {sheet.unclear.length > 0 && <p className="mt-3 text-xs text-faint">Couldn&apos;t read confidently: {sheet.unclear.join(", ")}.</p>}
    </section>
  );
}

function CostBreakdown({ lot }: { lot: LotView }) {
  const landed = lot.landed!;
  return (
    <details className="card group p-5" open>
      <summary className="flex cursor-pointer list-none items-baseline justify-between">
        <h2 className="text-lg font-semibold">Cost breakdown</h2>
        <span className="text-xs text-faint">
          ¥{Math.round(landed.jpyPerUnit)} = {money({ amount: 1, currency: landed.total.currency })}
          {landed.fxLive && landed.fxDate ? ` · ECB rate, ${shortDate(landed.fxDate)}` : " · fallback rate"}{" "}
          <span className="inline-block transition group-open:rotate-180">⌄</span>
        </span>
      </summary>
      <ul className="mt-3 divide-y divide-line text-sm">
        {landed.lines
          .filter((l) => l.amount.amount !== 0)
          .map((l) => (
            <li key={l.id} className="flex items-baseline justify-between gap-4 py-2.5">
              <span className="text-muted">
                {l.label}
                {l.isEstimate && l.low && l.high && l.low.amount !== l.high.amount && (
                  <span className="tabular block text-xs text-faint">
                    {money(l.low)}–{money(l.high).replace(/^[A-Z$]+/, "")}
                  </span>
                )}
              </span>
              <span className="tabular shrink-0">{money(l.amount)}</span>
            </li>
          ))}
        <li className="flex items-baseline justify-between pt-3 text-base font-semibold">
          <span>Total (est.)</span>
          <span className="tabular">{money(landed.total)}</span>
        </li>
      </ul>
      {landed.note && <p className="mt-3 text-xs text-faint">{landed.note}</p>}
    </details>
  );
}

function History({ lot }: { lot: LotView }) {
  const points: ChartPoint[] = [
    ...[...lot.relists].reverse().flatMap((r) => (r.openingBid ? [{ date: r.auctionDate, price: r.openingBid }] : [])),
    ...(lot.openingBid && lot.auctionDay ? [{ date: lot.auctionDay, price: lot.openingBid }] : []),
  ];
  if (lot.relists.length === 0 && points.length < 2) {
    return (
      <section className="card p-5">
        <h2 className="text-lg font-semibold">History</h2>
        <p className="mt-2 text-sm text-muted">First time Mitsuke has seen this car at auction.</p>
      </section>
    );
  }
  const first = lot.relists.at(-1)?.auctionDate;
  const changes = [...new Set(lot.relists.flatMap((r) => r.changes))];
  return (
    <section className="card p-5">
      <h2 className="text-lg font-semibold">History</h2>
      <p className="mt-1 text-sm text-muted">
        Seen at auction {lot.relists.length} time{lot.relists.length === 1 ? "" : "s"} before{first && <>, since {shortDate(first)}</>}.
      </p>
      {changes.length > 0 && <p className="mt-1 text-sm text-amber">Changed between auctions: {changes.join(", ")}.</p>}
      <div className="mt-3">
        <PriceChart points={points} />
      </div>
    </section>
  );
}

function LotSkeleton() {
  return (
    <div className="grid animate-pulse gap-6 lg:grid-cols-[1.15fr_1fr]">
      <div className="aspect-[4/3] rounded-[1.25rem] bg-raised" />
      <div className="space-y-4">
        <div className="h-9 w-3/4 rounded-lg bg-raised" />
        <div className="h-8 w-1/2 rounded-lg bg-raised" />
        <div className="h-36 rounded-[1.25rem] bg-raised" />
        <div className="h-20 rounded-[1.25rem] bg-raised" />
        <div className="h-64 rounded-[1.25rem] bg-raised" />
      </div>
    </div>
  );
}
