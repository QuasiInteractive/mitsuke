/**
 * "Then": what a car cost new in Japan, hand-checked against a published source (the link is shown on the page).
 * Keyed by chassis code. Only add a figure with a source; a wrong launch price on a public page is worse than none.
 * Checked 14 Oct 2026 against the Gulliver (221616.com) catalogue, cross-checked where noted. Before 2004 Japanese makers
 * quoted list prices before consumption tax, so those are labelled "launch list price", not "incl. tax".
 */
export type LaunchFact = {
  /** Grade the price is for, e.g. "GT-R" or "RZ (6-speed)". */
  grade: string;
  /** Launch month, YYYY-MM. */
  launched: string;
  /** New price in Japan, yen. */
  priceJpy: number;
  /** "launch list price", "incl. 5% tax" or similar. */
  taxNote: string;
  source: { label: string; url: string };
};

export type ModelHistory = {
  /** One or two sentences: what the car is and why people want it. */
  story: string;
  production?: string;
  launch: LaunchFact[];
};

const gulliver = (path: string) => ({ label: "Gulliver catalogue", url: `https://221616.com/catalog/${path}` });
const LIST = "launch list price";

export const HISTORY: Record<string, ModelHistory> = {
  BNR32: {
    story: "The car that brought the GT-R name back after 16 years: twin-turbo RB26, all-wheel drive, and a racing record that earned it the name Godzilla.",
    production: "Built 1989–1994, about 44,000 made (secondary sources; Nissan hasn't published a figure).",
    launch: [
      { grade: "GT-R (5-speed)", launched: "1989-08", priceJpy: 4_450_000, taxNote: LIST, source: gulliver("nissan/skyline/198905_199308/1501472/") },
      { grade: "GT-R V-spec", launched: "1993-02", priceJpy: 5_260_000, taxNote: LIST, source: gulliver("nissan/skyline/198905_199308/1507323/") },
    ],
  },
  JZA80: {
    story: "The A80 Supra: the 2JZ-GTE twin-turbo in the RZ, famous for taking huge power on standard internals, and a film star ever since.",
    production: "Sold in Japan May 1993 to August 2002; 31,372 registered there (Japanese Wikipedia).",
    launch: [
      { grade: "RZ (twin-turbo, 6-speed)", launched: "1993-05", priceJpy: 4_230_000, taxNote: LIST, source: gulliver("toyota/supra/199305_200207/1000910/") },
      { grade: "GZ (twin-turbo, auto)", launched: "1993-05", priceJpy: 4_550_000, taxNote: LIST, source: gulliver("toyota/supra/199305_200207/1000917/") },
      { grade: "SZ (non-turbo, 5-speed)", launched: "1993-05", priceJpy: 2_900_000, taxNote: LIST, source: gulliver("toyota/supra/199305_200207/1000911/") },
    ],
  },
  NA1: {
    story: "Honda's aluminium mid-engined supercar, developed with Ayrton Senna's input: the everyday-usable exotic.",
    production: "First generation sold 1990–2005.",
    launch: [
      { grade: "5-speed", launched: "1990-09", priceJpy: 8_003_000, taxNote: LIST, source: gulliver("honda/nsx/199009_200512/2002128/") },
      { grade: "4-speed auto", launched: "1990-09", priceJpy: 8_603_000, taxNote: LIST, source: gulliver("honda/nsx/199009_200512/2002129/") },
    ],
  },
  CN9A: {
    story: "The Evo IV: a new body, the 4G63 turned around and Active Yaw Control on the GSR. The start of Mitsubishi's rally-winning run.",
    production: "Sold 1996–1997; about 12,000 GSRs (Japanese Wikipedia).",
    launch: [
      { grade: "GSR", launched: "1996-08", priceJpy: 2_998_000, taxNote: LIST, source: gulliver("mitsubishi/lancer/199510_200009/4005454/") },
      { grade: "RS", launched: "1996-08", priceJpy: 2_498_000, taxNote: LIST, source: gulliver("mitsubishi/lancer/199510_200009/4005455/") },
    ],
  },
  CT9A: {
    story: "The last of the 4G63 Evos: the VIII brought Super AYC, the IX MIVEC timing. Many think it's the best of the line.",
    launch: [
      { grade: "Evo VIII GSR (6-speed)", launched: "2003-01", priceJpy: 3_298_000, taxNote: LIST, source: gulliver("mitsubishi/lancer/200102_200806/10002338/") },
      { grade: "Evo IX GSR (5-speed)", launched: "2005-03", priceJpy: 3_570_000, taxNote: "incl. 5% tax", source: gulliver("mitsubishi/lancer/200102_200806/10026561/") },
    ],
  },
  DC2: {
    story: "The Integra Type R: a hand-finished B18C, a seam-welded shell and a helical LSD. Often called the best front-wheel-drive car ever made.",
    production: "Sold 1995–2001.",
    launch: [
      { grade: "Type R (3-door)", launched: "1995-10", priceJpy: 2_228_000, taxNote: LIST, source: gulliver("honda/integra/199305_200107/2001619/") },
    ],
  },
  HZJ77HV: {
    story: "The 70 Series Land Cruiser with the 4.2-litre 1HZ diesel: ladder chassis, solid axles, built to work. The long-wheelbase wagon.",
    production: "The 1HZ four-door was sold in Japan 1990–1999.",
    launch: [
      { grade: "ZX 4-door (5-speed)", launched: "1990-04", priceJpy: 2_525_000, taxNote: LIST, source: gulliver("toyota/landcruiser70/198708_299912/10001590/") },
      { grade: "ZX 4-door (auto)", launched: "1990-04", priceJpy: 2_618_000, taxNote: LIST, source: gulliver("toyota/landcruiser70/198708_299912/10001591/") },
    ],
  },
};
