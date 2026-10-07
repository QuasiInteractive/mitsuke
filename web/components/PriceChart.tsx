import type { Money } from "@/lib/api";
import { money, shortDate } from "@/lib/format";

export type ChartPoint = { date: string; price: Money; label?: string };

/** A small line of opening bids over time: this car's earlier auctions plus its current listing. */
export function PriceChart({ points }: { points: ChartPoint[] }) {
  if (points.length < 2) return null;
  const values = points.map((p) => p.price.amount);
  const [min, max] = [Math.min(...values), Math.max(...values)];
  const span = max - min || 1;
  const w = 300;
  const h = 64;
  const xy = points.map((p, i) => [8 + (i * (w - 16)) / (points.length - 1), 10 + (1 - (p.price.amount - min) / span) * (h - 24)] as const);

  return (
    <figure>
      <svg viewBox={`0 0 ${w} ${h}`} className="h-20 w-full" role="img" aria-label="Opening bid at each auction">
        <polyline points={xy.map(([x, y]) => `${x},${y}`).join(" ")} fill="none" stroke="var(--color-accent)" strokeWidth="2" strokeLinejoin="round" />
        {xy.map(([x, y], i) => (
          <circle key={i} cx={x} cy={y} r={i === xy.length - 1 ? 4.5 : 3} fill={i === xy.length - 1 ? "var(--color-accent)" : "#0b0b0d"} stroke="var(--color-accent)" strokeWidth="1.5">
            <title>{`${shortDate(points[i].date)}: ${money(points[i].price)}`}</title>
          </circle>
        ))}
      </svg>
      <figcaption className="tabular mt-1 flex justify-between text-[11px] text-faint">
        <span>{shortDate(points[0].date)}</span>
        <span>
          {money(points[0].price, { compact: true })} → <span className="text-accent">{money(points.at(-1)!.price, { compact: true })}</span>
        </span>
        <span>{shortDate(points.at(-1)!.date)}</span>
      </figcaption>
    </figure>
  );
}
