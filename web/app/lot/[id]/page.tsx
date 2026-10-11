import { HowToGetIt } from "@/components/HowToGetIt";
import { FujiArt, ScoreRing } from "@/components/Brand";
import {
  CalendarDays, Gauge, Star, Cog, CircleDot, Wrench, Palette, Clock, ClipboardList, Calculator, LineChart, CarFront,
  Gavel, Ship, Receipt, FileCheck2, Coins, ShieldCheck, TriangleAlert, type LucideIcon,
} from "lucide-react";
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
      <div className="grid gap-8 pt-6 lg:grid-cols-[1.15fr_1fr] lg:items-start lg:pt-8">
        <div className="min-w-0 space-y-4 lg:sticky lg:top-24">
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

        <div className="min-w-0 space-y-4">
          <Header lot={lot} />
          <PriceCards lot={lot} />
          {lot.sheet?.redFlags.some((f) => f.severity === "High") && <RedFlags lot={lot} />}
          {lot.auctionEndsAt && <AuctionCard lot={lot} />}
          {lot.eligibility && <Eligibility lot={lot} />}
          {lot.eligibility && <HowToGetIt eligibility={lot.eligibility} builtYear={lot.year} />}
          {(lot.variant || lot.spec.length > 0) && <CarDetails lot={lot} />}
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
        auctionEndsAt={lot.auctionEndsAt}
      />
    </>
  );
}

function SectionHead({ icon: Icon, title, aside }: { icon: LucideIcon; title: string; aside?: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <h2 className="flex items-center gap-3 text-lg font-semibold">
        <span className="grid size-9 place-items-center rounded-xl border border-hairline bg-black/30 text-muted">
          <Icon className="size-[18px]" aria-hidden />
        </span>
        {title}
      </h2>
      {aside && <span className="text-xs text-faint">{aside}</span>}
    </div>
  );
}

function Header({ lot }: { lot: LotView }) {
  // The sheet can say the odometer is wrong; never show the number as if it were fact.
  const mileageDoubtful = lot.sheet?.redFlags.some((f) => f.severity === "High" && /mileage|odometer/i.test(f.title)) ?? false;
  const built = lot.year && lot.month ? `Built ${new Date(lot.year, lot.month - 1, 1).toLocaleDateString("en-AU", { month: "short", year: "numeric" })}` : null;
  const chips: [LucideIcon, string | null | false | undefined][] = [
    [CalendarDays, built],
    [Gauge, lot.mileageKm == null ? null : `${km(lot.mileageKm)}${mileageDoubtful ? " (unverified)" : ""}`],
    [Star, lot.grade && `Grade ${lot.grade}${lot.gradeIsRepaired ? " (repaired)" : ""}`],
    [Cog, lot.transmission],
    [CircleDot, lot.rightHandDrive == null ? null : lot.rightHandDrive ? "RHD" : "LHD"],
    [Wrench, lot.isModified && "Modified"],
    [Palette, lot.sheet?.colour],
  ];
  return (
    <div>
      <h1 className="text-3xl leading-tight font-bold tracking-tight sm:text-4xl">{lot.title}</h1>
      {lot.variant && (
        <p className="mt-1 text-lg font-semibold text-accent" title={lot.variant.reason}>
          {lot.variant.certain ? "" : "Likely "}
          {lot.variant.name}
        </p>
      )}
      <div className="mt-3 flex flex-wrap gap-2">
        {chips.filter(([, text]) => text).map(([Icon, text]) => (
          <span key={String(text)} className="inline-flex items-center gap-2 rounded-full border border-hairline bg-white/[0.03] px-3.5 py-1.5 text-sm">
            <Icon className="size-4 text-muted" aria-hidden />
            {text}
          </span>
        ))}
      </div>
    </div>
  );
}

function PriceCards({ lot }: { lot: LotView }) {
  const deal = lot.deal;
  return (
    <div className="grid gap-3 sm:grid-cols-[1.45fr_1fr]">
      <div className="card relative overflow-hidden p-5">
        <FujiArt className="absolute top-4 right-4 w-32 opacity-35" />
        <p className="relative text-sm text-muted">Est. landed in {lot.landed?.destination ?? "AU"}</p>
        <p className="tabular relative mt-1 text-4xl font-bold tracking-tight xl:text-[2.75rem]">{money(lot.landed?.total)}</p>
        {lot.landed && (
          <p className="tabular mt-1 text-xs text-faint">
            range {money(lot.landed.low)}–{money(lot.landed.high).replace(/^[A-Z$]+/, "")}
          </p>
        )}
        <p className="relative mt-3 text-sm text-muted">
          Opening bid <span className="tabular text-text">{money(lot.openingBid)}</span>
          {lot.auctionHouse && <> · {lot.auctionHouse}</>}
        </p>
        {lot.openingBid && (
          <p className="relative mt-3 border-t border-hairline pt-3 text-xs text-faint">Based on the opening bid, which is a floor: cars usually sell for more.</p>
        )}
      </div>
      <div className={`card p-5 ${deal?.score != null && deal.score >= 80 ? "card-alert" : ""}`}>
        <p className="text-sm text-muted">Deal score</p>
        {deal?.score != null ? (
          <>
            <div className="mt-2 flex items-center gap-3">
              <ScoreRing score={deal.score} />
              <p className="tabular text-4xl font-bold">
                {deal.score}
                <span className="text-lg text-muted">/100</span>
              </p>
            </div>
            <p className={`mt-2 font-semibold ${deal.score >= 70 ? "text-accent" : deal.score >= 40 ? "text-text" : "text-amber"}`}>{deal.label}</p>
            <p className="mt-2 text-xs text-faint">
              vs {deal.comparableCount} cars{deal.confidence === "Low" ? " · low confidence" : ""}
            </p>
            {deal.openingLow && deal.openingHigh && (
              <p className="mt-2 text-xs text-muted">
                Similar cars opened at <span className="tabular text-text">{money(deal.openingLow)}–{money(deal.openingHigh).replace(/^[^\d]+/, "")}</span>
              </p>
            )}
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
            <TriangleAlert className="mt-0.5 size-5 shrink-0 text-accent" aria-hidden />
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
      <Clock className="size-7 shrink-0 text-accent" aria-hidden />
      <div className="flex-1">
        <p className="text-sm text-muted">Bids close in</p>
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
      <SectionHead
        icon={ClipboardList}
        title="Condition"
        aside={<>from the auction sheet{sheet.overallGrade && ` · grade ${sheet.overallGrade}`}{lot.interiorGrade && ` · interior ${lot.interiorGrade}`}</>}
      />
      <p className="mt-3 text-sm leading-relaxed text-muted">{sheet.summary}</p>
      <div className="mt-4 grid items-center gap-4 sm:grid-cols-[auto_1fr]">
        <div className="justify-self-center">
          <CarDiagram damage={sheet.damage} />
        </div>
        <ul className="space-y-2">
          {sheet.damage.map((d, i) => (
            <li key={i} className="flex items-start gap-3 rounded-xl border border-hairline bg-black/25 px-3.5 py-2.5 text-sm">
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

function CarDetails({ lot }: { lot: LotView }) {
  const unchecked = lot.spec.some((s) => !s.checked);
  return (
    <section className="card p-5">
      <SectionHead icon={CarFront} title="The car" />
      {lot.variant && <p className="mt-1 text-sm text-muted">{lot.variant.reason}</p>}
      {lot.spec.length > 0 && (
        <dl className="mt-4 grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm">
          {lot.spec.map((s) => (
            <div key={s.label} className="contents">
              <dt className="text-muted">{s.label}</dt>
              <dd>
                {s.value}
                {!s.checked && <span className="ml-1 text-faint">*</span>}
              </dd>
            </div>
          ))}
        </dl>
      )}
      {unchecked && <p className="mt-3 text-xs text-faint">* Machine-read from the auction sheet and not cross-checked. Confirm on the sheet image.</p>}
    </section>
  );
}

const VERDICT = {
  Yes: { mark: "✓", tone: "text-good border-good/40 bg-good/10" },
  Maybe: { mark: "?", tone: "text-amber border-amber/40 bg-amber/10" },
  No: { mark: "✕", tone: "text-accent border-accent/40 bg-accent/10" },
} as const;

function Pathway({ p }: { p: { name: string; verdict: "Yes" | "Maybe" | "No"; reason: string } }) {
  return (
    <li className="flex gap-3 text-sm">
      <span className={`mt-0.5 shrink-0 font-bold ${VERDICT[p.verdict].tone.split(" ")[0]}`}>{VERDICT[p.verdict].mark}</span>
      <span>
        <span className="font-medium">{p.name}.</span> <span className="text-muted">{p.reason}</span>
      </span>
    </li>
  );
}

function Eligibility({ lot }: { lot: LotView }) {
  const e = lot.eligibility!;
  const v = VERDICT[e.verdict];
  return (
    <section className="card p-5">
      <div className="flex items-start gap-3">
        <span className={`grid size-9 shrink-0 place-items-center rounded-full border text-lg font-bold ${v.tone}`}>{v.mark}</span>
        <div>
          <p className="text-sm text-muted">Can you import it to {e.destination}?</p>
          <h2 className="text-lg font-semibold">{e.headline}</h2>
        </div>
      </div>
      {/* When one route clearly works, show it; the rest fold away. Otherwise every route matters. */}
      <ul className="mt-4 space-y-3">
        {(e.verdict === "Yes" ? e.pathways.filter((p) => p.verdict === "Yes") : e.pathways).map((p) => <Pathway key={p.name} p={p} />)}
      </ul>
      {e.verdict === "Yes" && e.pathways.some((p) => p.verdict !== "Yes") && (
        <details className="group mt-3">
          <summary className="cursor-pointer list-none text-sm text-muted hover:text-text">
            Other pathways ({e.pathways.filter((p) => p.verdict !== "Yes").length}) <span className="inline-block transition group-open:rotate-180">⌄</span>
          </summary>
          <ul className="mt-3 space-y-3">
            {e.pathways.filter((p) => p.verdict !== "Yes").map((p) => <Pathway key={p.name} p={p} />)}
          </ul>
        </details>
      )}
      {e.links.length > 0 && (
        <details className="group mt-4 border-t border-hairline pt-3">
          <summary className="cursor-pointer list-none text-sm text-muted hover:text-text">
            Official links ({e.links.length}) <span className="inline-block transition group-open:rotate-180">⌄</span>
          </summary>
          <p className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-sm">
            {e.links.map((l) => (
              <a key={l.url} href={l.url} target="_blank" rel="noopener noreferrer" className="text-accent hover:underline">
                {l.label} ↗
              </a>
            ))}
          </p>
        </details>
      )}
      <p className="mt-3 text-xs text-faint">Rules last checked {e.rulesCheckedOn}. Confirm with your exporter or a compliance workshop before bidding.</p>
    </section>
  );
}

/** An icon for each kind of cost line, matched on the engine's id or label. */
function costIcon(id: string, label: string): LucideIcon {
  const t = `${id} ${label}`.toLowerCase();
  if (/vehicle|car|price/.test(t)) return CarFront;
  if (/auction|export|agent|fee/.test(t)) return Gavel;
  if (/ship|freight|transport|port/.test(t)) return Ship;
  if (/duty|gst|tax|lct/.test(t)) return Receipt;
  if (/compli|raw|inspect|rego|regist/.test(t)) return FileCheck2;
  if (/insur/.test(t)) return ShieldCheck;
  return Coins;
}

function CostBreakdown({ lot }: { lot: LotView }) {
  const landed = lot.landed!;
  return (
    <details className="card group p-5" open>
      <summary className="flex cursor-pointer list-none items-center justify-between gap-3">
        <SectionHead icon={Calculator} title="Cost breakdown" />
        <span className="text-right text-xs text-faint">
          ¥{Math.round(landed.jpyPerUnit)} = {money({ amount: 1, currency: landed.total.currency })}
          {landed.fxLive && landed.fxDate ? ` · ECB rate, ${shortDate(landed.fxDate)}` : " · fallback rate"}{" "}
          <span className="inline-block transition group-open:rotate-180">⌄</span>
        </span>
      </summary>
      <ul className="mt-4 divide-y divide-hairline text-sm">
        {landed.lines
          .filter((l) => l.amount.amount !== 0)
          .map((l) => (
            <li key={l.id} className="flex items-center justify-between gap-4 py-3">
              <span className="flex items-start gap-3 text-muted">
                {(() => {
                  const Icon = costIcon(l.id, l.label);
                  return <Icon className="mt-0.5 size-4 shrink-0 text-faint" aria-hidden />;
                })()}
                <span>
                {l.label}
                {l.isEstimate && l.low && l.high && l.low.amount !== l.high.amount && (
                  <span className="tabular block text-xs text-faint">
                    {money(l.low)}–{money(l.high).replace(/^[A-Z$]+/, "")}
                  </span>
                )}
                </span>
              </span>
              <span className="tabular shrink-0 text-text">{money(l.amount)}</span>
            </li>
          ))}
        <li className="flex items-baseline justify-between pt-4 text-lg font-bold">
          <span>Total <span className="text-sm font-normal text-muted">(est.)</span></span>
          <span className="tabular text-xl">{money(landed.total)}</span>
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
        <SectionHead icon={LineChart} title="History" />
        <p className="mt-2 text-sm text-muted">First time Mitsuke has seen this car at auction.</p>
      </section>
    );
  }
  const first = lot.relists.at(-1)?.auctionDate;
  const changes = [...new Set(lot.relists.flatMap((r) => r.changes))];
  return (
    <section className="card p-5">
      <SectionHead icon={LineChart} title="History" />
      <p className="mt-1 text-sm text-muted">
        Seen at auction {lot.relists.length} time{lot.relists.length === 1 ? "" : "s"} before{first && <>, since {shortDate(first)}</>}.
        {lot.relists.length >= 2 && (
          <span className="block text-faint">
            A car that keeps coming back usually has a reserve above its opening bid: expect to pay more than it opens at.
          </span>
        )}
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
