import { TrendingDown, TrendingUp, Minus, Activity } from "lucide-react";
import type { MarketMeter as Meter } from "@/lib/api";
import { money, shortDate } from "@/lib/format";

const yen = (amount: number) => money({ amount, currency: "JPY" }, { compact: true });

const SIGNAL = {
  Below: { text: "Below usual", tone: "text-good", ring: "border-good/50 bg-good/10", glow: "var(--color-good)" },
  About: { text: "About usual", tone: "text-text", ring: "border-hairline bg-white/[0.04]", glow: "var(--color-cyan)" },
  Above: { text: "Above usual", tone: "text-amber", ring: "border-amber/50 bg-amber/10", glow: "var(--color-amber)" },
} as const;

/**
 * The stock chart for cars: where this lot's opening bid sits among every similar car Mitsuke has seen lately,
 * the weekly typical, and how many are at auction now. Opening bids, not sale prices, and it says so.
 */
export function MarketMeter({ meter }: { meter: Meter }) {
  const s = meter.signal ? SIGNAL[meter.signal] : null;
  return (
    <section className="card relative overflow-hidden p-5">
      <div className="pointer-events-none absolute inset-0 grid-bg opacity-40" aria-hidden />
      <div className="relative">
        <div className="flex items-center justify-between gap-3">
          <h2 className="flex items-center gap-3 text-lg font-semibold">
            <span className="grid size-9 place-items-center rounded-xl border border-hairline bg-black/30 text-muted">
              <Activity className="size-[18px]" aria-hidden />
            </span>
            Market meter
          </h2>
          <span className="text-xs text-faint">{meter.name} · since {shortDate(meter.since)}</span>
        </div>

        <div className="mt-4 flex flex-wrap items-end justify-between gap-4">
          <div>
            {s && (
              <span className={`inline-flex items-center gap-2 rounded-full border px-3 py-1 text-xs font-bold tracking-[0.14em] uppercase ${s.ring} ${s.tone}`}>
                <span className="size-1.5 rounded-full" style={{ background: s.glow, boxShadow: `0 0 8px ${s.glow}` }} />
                {s.text}
              </span>
            )}
            {meter.vsTypicalPercent != null ? (
              <p className={`figure mt-2 text-5xl ${s?.tone ?? ""}`}>
                {meter.vsTypicalPercent > 0 ? "+" : ""}
                {meter.vsTypicalPercent}%
                <span className="ml-2 text-base text-muted not-italic">vs typical</span>
              </p>
            ) : (
              <p className="figure mt-2 text-4xl">{yen(meter.typical)}</p>
            )}
          </div>
          <dl className="grid grid-cols-3 gap-5 text-right text-xs">
            <Stat label="Typical" value={yen(meter.typical)} />
            <Stat label="At auction" value={String(meter.onAuctionNow)} />
            <Stat label="Trend" value={<Trend percent={meter.trendPercent} />} />
          </dl>
        </div>

        <Gauge meter={meter} />
        <Weeks meter={meter} />

        <p className="mt-4 text-sm text-muted">{meter.summary}</p>
        <p className="mt-2 text-xs text-faint">
          {meter.cars} opening bids from the last 12 weeks, one per car per week. Opening bids are a floor, not sale prices: the meter switches to real
          results once exporter partners report them.
        </p>
      </div>
    </section>
  );
}

function Stat({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <dt className="label">{label}</dt>
      <dd className="figure mt-1 text-lg">{value}</dd>
    </div>
  );
}

function Trend({ percent }: { percent: number | null }) {
  if (percent == null) return <span className="text-sm text-faint not-italic">building</span>;
  const Icon = percent > 2 ? TrendingUp : percent < -2 ? TrendingDown : Minus;
  return (
    <span className={`inline-flex items-center gap-1 ${percent < -2 ? "text-good" : percent > 2 ? "text-amber" : ""}`}>
      <Icon className="size-4" aria-hidden />
      {Math.abs(percent).toFixed(0)}%
    </span>
  );
}

/** Every opening bid as a dot on one line, the usual range shaded, the typical ticked, this lot glowing. */
function Gauge({ meter }: { meter: Meter }) {
  const values = [...meter.spread, ...(meter.this != null ? [meter.this] : [])];
  const lo = Math.min(...values);
  const hi = Math.max(...values);
  const span = hi - lo || 1;
  const x = (v: number) => 12 + ((v - lo) / span) * 576;
  const glow = meter.signal ? SIGNAL[meter.signal].glow : "var(--color-accent)";

  return (
    <figure className="mt-5">
      <svg viewBox="0 0 600 74" className="w-full" role="img" aria-label={`Opening bids from ${yen(lo)} to ${yen(hi)}, typical ${yen(meter.typical)}`}>
        <defs>
          <linearGradient id="mm-track" x1="0" x2="1">
            <stop offset="0" stopColor="var(--color-good)" stopOpacity="0.55" />
            <stop offset="0.5" stopColor="var(--color-cyan)" stopOpacity="0.35" />
            <stop offset="1" stopColor="var(--color-amber)" stopOpacity="0.55" />
          </linearGradient>
        </defs>
        <rect x="12" y="34" width="576" height="6" rx="3" fill="url(#mm-track)" opacity="0.35" />
        <rect x={x(meter.low)} y="30" width={Math.max(4, x(meter.high) - x(meter.low))} height="14" rx="7" fill="rgb(255 255 255 / 0.06)" stroke="rgb(255 255 255 / 0.14)" />
        {meter.spread.map((v, i) => (
          <circle key={i} cx={x(v)} cy={37 + ((i % 3) - 1) * 4} r="2.2" fill="rgb(242 244 255 / 0.45)" />
        ))}
        <line x1={x(meter.typical)} x2={x(meter.typical)} y1="22" y2="52" stroke="var(--color-text)" strokeWidth="2" />
        <text x={x(meter.typical)} y="64" textAnchor="middle" className="fill-[var(--color-muted)] text-[11px]">typical {yen(meter.typical)}</text>
        {meter.this != null && (
          <g>
            <circle cx={x(meter.this)} cy="37" r="11" fill={glow} opacity="0.25" />
            <circle cx={x(meter.this)} cy="37" r="6.5" fill={glow} stroke="#07080d" strokeWidth="2" />
            <text x={Math.min(560, Math.max(40, x(meter.this)))} y="14" textAnchor="middle" className="text-[12px] font-bold" fill={glow}>
              this car {yen(meter.this)}
            </text>
          </g>
        )}
      </svg>
      <figcaption className="tabular flex justify-between px-1 text-[11px] text-faint">
        <span>{yen(lo)} cheapest</span>
        <span>usual {yen(meter.low)}–{yen(meter.high).replace("¥", "")}</span>
        <span>{yen(hi)} dearest</span>
      </figcaption>
    </figure>
  );
}

/** The weekly typical as a line over bars of supply. Waits for two weeks rather than drawing a dot. */
function Weeks({ meter }: { meter: Meter }) {
  const weeks = meter.weeks;
  if (weeks.length < 2) {
    return (
      <p className="mt-4 rounded-xl border border-dashed border-hairline px-4 py-3 text-xs text-faint">
        Weekly chart builds as Mitsuke watches: one week of data so far.
      </p>
    );
  }
  const w = 600;
  const h = 110;
  const maxCars = Math.max(...weeks.map((k) => k.cars));
  const prices = weeks.map((k) => k.typical);
  const [pMin, pMax] = [Math.min(...prices), Math.max(...prices)];
  const pSpan = pMax - pMin || pMax * 0.1 || 1;
  const step = (w - 40) / weeks.length;
  const cx = (i: number) => 20 + step * (i + 0.5);
  const cy = (v: number) => 14 + (1 - (v - pMin) / pSpan) * (h - 46);
  const line = weeks.map((k, i) => `${cx(i)},${cy(k.typical)}`).join(" ");

  return (
    <figure className="mt-5">
      <svg viewBox={`0 0 ${w} ${h}`} className="w-full" role="img" aria-label="Typical opening bid by week, with the number of cars offered">
        <defs>
          <linearGradient id="mm-area" x1="0" x2="0" y1="0" y2="1">
            <stop offset="0" stopColor="var(--color-accent)" stopOpacity="0.35" />
            <stop offset="1" stopColor="var(--color-accent)" stopOpacity="0" />
          </linearGradient>
        </defs>
        {weeks.map((k, i) => {
          const bh = Math.max(3, (k.cars / maxCars) * 26);
          return <rect key={k.weekOf} x={cx(i) - step * 0.3} y={h - 18 - bh} width={step * 0.6} height={bh} rx="2" fill="rgb(25 230 255 / 0.22)" />;
        })}
        <polygon points={`${cx(0)},${h - 18} ${line} ${cx(weeks.length - 1)},${h - 18}`} fill="url(#mm-area)" />
        <polyline points={line} fill="none" stroke="var(--color-accent)" strokeWidth="2.5" strokeLinejoin="round" style={{ filter: "drop-shadow(0 0 6px rgb(255 45 85 / 0.6))" }} />
        {weeks.map((k, i) => (
          <g key={k.weekOf}>
            <circle cx={cx(i)} cy={cy(k.typical)} r="3.5" fill="#07080d" stroke="var(--color-accent)" strokeWidth="2">
              <title>{`Week of ${shortDate(k.weekOf)}: typical ${yen(k.typical)}, ${k.cars} cars`}</title>
            </circle>
            <text x={cx(i)} y={h - 4} textAnchor="middle" className="fill-[var(--color-faint)] text-[10px]">{shortDate(k.weekOf)}</text>
          </g>
        ))}
      </svg>
      <figcaption className="flex justify-end gap-4 text-[11px] text-faint">
        <span className="inline-flex items-center gap-1.5"><span className="h-0.5 w-3 bg-accent" /> typical opening bid</span>
        <span className="inline-flex items-center gap-1.5"><span className="size-2 rounded-sm bg-cyan/40" /> cars offered</span>
      </figcaption>
    </figure>
  );
}

/** A compact meter for a watchlist card: typical, the usual range, supply and trend in one line. */
export function MarketStrip({ meter }: { meter: Meter }) {
  return (
    <div className="rounded-2xl border border-hairline bg-black/25 px-4 py-3">
      <div className="flex items-center justify-between gap-3">
        <span className="label flex items-center gap-1.5"><Activity className="size-3.5 text-accent" aria-hidden /> Market</span>
        <span className="text-[11px] text-faint">{meter.cars} bids since {shortDate(meter.since)}</span>
      </div>
      <div className="mt-2 grid grid-cols-3 gap-3">
        <Stat label="Typical" value={yen(meter.typical)} />
        <Stat label="At auction" value={String(meter.onAuctionNow)} />
        <Stat label="Trend" value={<Trend percent={meter.trendPercent} />} />
      </div>
      <p className="tabular mt-1 text-[11px] text-faint">Usually opens {yen(meter.low)}–{yen(meter.high).replace("¥", "")}</p>
    </div>
  );
}
