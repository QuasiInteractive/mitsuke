import type { MarketMeter, MarketMonth } from "@/lib/api";
import { money } from "@/lib/format";

const yen = (amount: number) => money({ amount, currency: "JPY" }, { compact: true });

type Point = { label: string; typical: number; low: number; high: number; min: number; max: number; cars: number };

const monthLabel = (iso: string) => {
  const d = new Date(iso + "T00:00:00Z");
  return `${d.toLocaleDateString("en-AU", { month: "short", timeZone: "UTC" })} '${String(d.getUTCFullYear()).slice(2)}`;
};
const weekLabel = (iso: string) => new Date(iso + "T00:00:00Z").toLocaleDateString("en-AU", { day: "numeric", month: "short", timeZone: "UTC" });

/**
 * A model's opening bids over time: the usual range as a band, the typical as a glowing line, the cheapest and dearest
 * as faint ticks, cars offered as bars. Monthly once there are a few months, weekly until then.
 */
export function HistoryChart({ months, meter }: { months: MarketMonth[]; meter: MarketMeter | null }) {
  // Drawn twice: wide for desktops, narrow for phones, so labels stay readable at both sizes.
  return (
    <>
      <div className="hidden sm:block"><Chart months={months} meter={meter} w={720} h={300} /></div>
      <div className="sm:hidden"><Chart months={months} meter={meter} w={380} h={300} /></div>
    </>
  );
}

function Chart({ months, meter, w, h }: { months: MarketMonth[]; meter: MarketMeter | null; w: number; h: number }) {
  const monthly = months.length >= 3;
  const points: Point[] = monthly
    ? months.map((m) => ({ label: monthLabel(m.month), typical: m.typical, low: m.low, high: m.high, min: m.min, max: m.max, cars: m.cars }))
    : (meter?.weeks ?? []).map((w) => ({ label: weekLabel(w.weekOf), typical: w.typical, low: w.typical, high: w.typical, min: w.typical, max: w.typical, cars: w.cars }));

  if (points.length < 2) {
    return (
      <div className="grid h-56 place-items-center rounded-2xl border border-dashed border-hairline px-6 text-center text-sm text-faint">
        The chart draws itself as Mitsuke watches this car: a couple more weeks of auctions and it starts.
      </div>
    );
  }

  const top = 18;
  const bottom = 62;
  const left = 56;
  const right = 14;
  const lo = Math.min(...points.map((p) => p.min)) * 0.92;
  const hi = Math.max(...points.map((p) => p.max)) * 1.04;
  const span = hi - lo || hi || 1;
  const step = (w - left - right) / points.length;
  const x = (i: number) => left + step * (i + 0.5);
  const y = (v: number) => top + (1 - (v - lo) / span) * (h - top - bottom);
  const maxCars = Math.max(...points.map((p) => p.cars));
  const ticks = [lo + span * 0.1, lo + span * 0.5, lo + span * 0.9];
  const band = [...points.map((p, i) => `${x(i)},${y(p.high)}`), ...[...points].reverse().map((p, i) => `${x(points.length - 1 - i)},${y(p.low)}`)].join(" ");
  const line = points.map((p, i) => `${x(i)},${y(p.typical)}`).join(" ");

  return (
    <figure>
      <svg viewBox={`0 0 ${w} ${h}`} className="w-full" role="img" aria-label={`Typical opening bid by ${monthly ? "month" : "week"}`}>
        <defs>
          <linearGradient id={`hc-area-${w}`} x1="0" x2="0" y1="0" y2="1">
            <stop offset="0" stopColor="var(--color-accent)" stopOpacity="0.28" />
            <stop offset="1" stopColor="var(--color-accent)" stopOpacity="0.04" />
          </linearGradient>
        </defs>
        {ticks.map((t) => (
          <g key={t}>
            <line x1={left} x2={w - right} y1={y(t)} y2={y(t)} stroke="rgb(255 255 255 / 0.06)" strokeDasharray="3 5" />
            <text x={left - 8} y={y(t) + 4} textAnchor="end" fontSize="11" fill="var(--color-faint)">{yen(t)}</text>
          </g>
        ))}
        {points.map((p, i) => {
          const bh = Math.max(3, (p.cars / maxCars) * 34);
          return <rect key={`b${i}`} x={x(i) - step * 0.28} y={h - bottom + 22 - bh} width={step * 0.56} height={bh} rx="2" fill="rgb(25 230 255 / 0.2)" />;
        })}
        {monthly && <polygon points={band} fill={`url(#hc-area-${w})`} />}
        {monthly &&
          points.map((p, i) => (
            <g key={`e${i}`} stroke="rgb(242 244 255 / 0.35)" strokeWidth="1.5">
              <line x1={x(i) - 5} x2={x(i) + 5} y1={y(p.max)} y2={y(p.max)} />
              <line x1={x(i) - 5} x2={x(i) + 5} y1={y(p.min)} y2={y(p.min)} />
            </g>
          ))}
        <polyline points={line} fill="none" stroke="var(--color-accent)" strokeWidth="3" strokeLinejoin="round" style={{ filter: "drop-shadow(0 0 8px rgb(255 45 85 / 0.6))" }} />
        {points.map((p, i) => (
          <g key={`p${i}`}>
            <circle cx={x(i)} cy={y(p.typical)} r="4.5" fill="#07080d" stroke="var(--color-accent)" strokeWidth="2.5">
              <title>{`${p.label}: typical ${yen(p.typical)}${monthly ? `, usually ${yen(p.low)}–${yen(p.high)}, cheapest ${yen(p.min)}, dearest ${yen(p.max)}` : ""}, ${p.cars} cars`}</title>
            </circle>
            {(points.length <= (w < 500 ? 6 : 12) || i % Math.ceil(points.length / (w < 500 ? 6 : 12)) === 0) && (
              <text x={x(i)} y={h - 22} textAnchor="middle" fontSize="11" fill="var(--color-faint)">{p.label}</text>
            )}
          </g>
        ))}
      </svg>
      <figcaption className="mt-2 flex flex-wrap justify-end gap-x-5 gap-y-1 text-[11px] text-faint">
        <span className="inline-flex items-center gap-1.5"><span className="h-0.5 w-4 bg-accent" /> typical opening bid</span>
        {monthly && <span className="inline-flex items-center gap-1.5"><span className="size-2.5 rounded-sm bg-accent/25" /> usual range (10th–90th percentile)</span>}
        {monthly && <span className="inline-flex items-center gap-1.5"><span className="h-px w-3 bg-white/40" /> cheapest and dearest</span>}
        <span className="inline-flex items-center gap-1.5"><span className="size-2.5 rounded-sm bg-cyan/30" /> cars offered</span>
        <span>{monthly ? "by month" : "by week, until there are a few months"}</span>
      </figcaption>
    </figure>
  );
}
