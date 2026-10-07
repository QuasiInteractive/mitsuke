import type { Money } from "./api";

const SYMBOL: Record<string, string> = { AUD: "A$", NZD: "NZ$", USD: "US$", JPY: "¥" };

export function money(m: Money | null | undefined, opts: { compact?: boolean } = {}): string {
  if (!m) return "—";
  const symbol = SYMBOL[m.currency] ?? `${m.currency} `;
  if (opts.compact && m.currency === "JPY" && Math.abs(m.amount) >= 1_000_000) {
    return `${symbol}${(m.amount / 1_000_000).toLocaleString("en-AU", { maximumFractionDigits: 2 })}M`;
  }
  return `${symbol}${Math.round(m.amount).toLocaleString("en-AU")}`;
}

export const km = (n: number | null | undefined) => (n == null ? "—" : `${n.toLocaleString("en-AU")} km`);

/** "Thu 8 Oct" from an ISO date (YYYY-MM-DD) without timezone surprises. */
export function day(iso: string | null | undefined): string {
  if (!iso) return "—";
  const [y, m, d] = iso.split("-").map(Number);
  return new Date(Date.UTC(y, m - 1, d)).toLocaleDateString("en-AU", { weekday: "short", day: "numeric", month: "short", timeZone: "UTC" });
}

export function shortDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-AU", { day: "numeric", month: "short" });
}

/** Damage-code severity for colouring diagram markers, from Kensa-ya's code weights. */
export function damageSeverity(code: string): "serious" | "moderate" | "minor" | "unknown" {
  const letter = code.trim().toUpperCase().match(/^[A-Z]{1,2}/)?.[0];
  if (!letter) return "unknown";
  if (["S", "C", "X", "RX"].includes(letter)) return "serious";
  if (["U", "B", "W", "XX", "Y"].includes(letter)) return "moderate";
  if (["A", "P", "H", "E", "G", "R", "K", "T"].includes(letter)) return "minor";
  return "unknown";
}
