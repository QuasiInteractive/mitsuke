import type { SheetDamage } from "@/lib/api";
import { damageSeverity } from "@/lib/format";

// Top-down car, front at the top (viewBox 0 0 120 240). Points per Kensa-ya damage location, by label.
const POINTS: Record<string, [number, number]> = {
  "front bumper": [60, 14],
  bonnet: [60, 45],
  windscreen: [60, 74],
  roof: [60, 112],
  "rear window": [60, 156],
  "boot lid / tailgate": [60, 190],
  "rear bumper": [60, 224],
  "left front wing": [20, 50],
  "right front wing": [100, 50],
  "left front door": [16, 96],
  "right front door": [104, 96],
  "left rear door": [16, 132],
  "right rear door": [104, 132],
  "left rear quarter": [20, 178],
  "right rear quarter": [100, 178],
  "left sill": [9, 115],
  "right sill": [111, 115],
  "left mirror": [6, 78],
  "right mirror": [114, 78],
};

export const SEVERITY_COLOUR = {
  serious: "var(--color-accent)",
  moderate: "var(--color-orange)",
  minor: "var(--color-amber)",
  unknown: "var(--color-faint)",
} as const;

/** The sheet's damage marks drawn on a car outline. Marks for wheels, underbody, interior etc. are listed, not drawn. */
export function CarDiagram({ damage }: { damage: SheetDamage[] }) {
  // Spread marks that share a panel so none hide another.
  const seen = new Map<string, number>();
  const marks = damage.flatMap((d) => {
    const p = POINTS[d.location.toLowerCase()];
    if (!p) return [];
    const n = seen.get(d.location) ?? 0;
    seen.set(d.location, n + 1);
    return [{ d, x: p[0] + (n % 2 === 0 ? 1 : -1) * Math.ceil(n / 2) * 7, y: p[1] }];
  });

  return (
    <svg viewBox="-4 0 128 240" className="h-72 w-auto" role="img" aria-label="Damage marks from the auction sheet">
      <rect x="12" y="4" width="96" height="232" rx="34" fill="#1f1f26" stroke="#3f3f4a" strokeWidth="2" />
      <path d="M24 66 Q60 54 96 66 L90 86 Q60 80 30 86 Z" fill="#2b2b35" stroke="#3f3f4a" />
      <rect x="28" y="88" width="64" height="58" rx="10" fill="#18181e" stroke="#3f3f4a" />
      <path d="M30 148 Q60 154 90 148 L94 166 Q60 172 26 166 Z" fill="#2b2b35" stroke="#3f3f4a" />
      <line x1="12" y1="114" x2="108" y2="114" stroke="#3f3f4a" strokeDasharray="3 3" />
      <rect x="0" y="72" width="10" height="12" rx="3" fill="#2b2b35" />
      <rect x="110" y="72" width="10" height="12" rx="3" fill="#2b2b35" />
      {marks.map(({ d, x, y }, i) => (
        <g key={i}>
          <title>{`${d.code} · ${d.location}${d.name ? ` · ${d.name}` : ""}`}</title>
          <circle cx={x} cy={y} r="6.5" fill={SEVERITY_COLOUR[damageSeverity(d.code)]} opacity="0.25" />
          <circle cx={x} cy={y} r="3.5" fill={SEVERITY_COLOUR[damageSeverity(d.code)]} stroke="#0b0b0d" strokeWidth="1" />
        </g>
      ))}
    </svg>
  );
}
