import "server-only";

// Types mirror Mitsuke.Api's JSON (src/Mitsuke.Api). The web app calls the API from its server only.

export type Money = { amount: number; currency: string };
export type CostLine = { id: string; label: string; amount: Money; low: Money | null; high: Money | null; isEstimate: boolean; note: string | null };
export type LandedEstimate = {
  destination: string;
  total: Money;
  low: Money;
  high: Money;
  jpyPerUnit: number;
  fxDate: string | null;
  fxLive: boolean;
  lines: CostLine[];
  note: string | null;
};
export type DealScore = {
  score: number | null;
  label: string;
  confidence: "None" | "Low" | "Normal";
  comparableCount: number;
  typical: Money | null;
  belowTypical: Money | null;
  basis: string;
};
export type SheetFlag = { severity: "Info" | "Medium" | "High"; title: string; detail: string };
export type SheetDamage = { code: string; location: string; name: string | null; size: string | null; summary: string | null; note: string | null };
export type SheetReport = {
  sheetUrl: string;
  decodedAt: string;
  isAuctionSheet: boolean;
  summary: string;
  redFlags: SheetFlag[];
  damage: SheetDamage[];
  overallGrade: string | null;
  interiorGrade: string | null;
  colour: string | null;
  mileageKm: number | null;
  modifications: string[];
  positives: string[];
  watchOut: string[];
  unclear: string[];
};
export type Relist = { auctionDate: string; auctionHouse: string | null; lotNumber: string | null; mileageKm: number | null; openingBid: Money | null; confidence: string | null; changes: string[] };
export type PricePoint = { observedAt: string; price: Money; kind: string };

export type LotView = {
  id: string;
  title: string;
  make: string;
  model: string;
  modelCode: string | null;
  isModified: boolean;
  year: number | null;
  mileageKm: number | null;
  grade: string | null;
  gradeIsRepaired: boolean;
  transmission: string | null;
  rightHandDrive: boolean | null;
  auctionHouse: string | null;
  lotNumber: string | null;
  auctionEndsAt: string | null;
  auctionDay: string | null;
  openingBid: Money | null;
  landed: LandedEstimate | null;
  deal: DealScore | null;
  sheet: SheetReport | null;
  photos: string[];
  sheetImage: string | null;
  relists: Relist[];
  priceHistory: PricePoint[];
  interiorGrade: string | null;
  attribution: string;
  disclaimer: string;
};

export type LotCard = {
  id: string;
  title: string;
  mileageKm: number | null;
  grade: string | null;
  photo: string | null;
  openingBid: Money | null;
  landedTotal: Money | null;
  dealScore: number | null;
  dealLabel: string | null;
  auctionDay: string | null;
  highFlags: number;
  watchlistName: string;
  alertedAt: string;
};

export type WatchlistSummary = {
  watchlist: { id: string; name: string; make: string; model: string; destination: string; maxLanded: Money | null; modelCodes: string[] };
  matchCount: number;
  lastMatchAt: string | null;
};

export const API_URL = process.env.MITSUKE_API_URL ?? "http://localhost:5107";

async function get<T>(path: string): Promise<T | null> {
  // Live data (prices, countdowns): never cached. Pages stream it inside <Suspense>.
  const res = await fetch(`${API_URL}${path}`, { cache: "no-store" });
  if (res.status === 404) return null;
  if (!res.ok) throw new Error(`Mitsuke API ${path} returned ${res.status}`);
  return (await res.json()) as T;
}

export const getLot = (id: string) => get<LotView>(`/api/lots/${encodeURIComponent(id)}`);
export const getMatches = async () => (await get<LotCard[]>("/api/matches?limit=24")) ?? [];
export const getWatchlists = async () => (await get<WatchlistSummary[]>("/api/watchlists")) ?? [];
