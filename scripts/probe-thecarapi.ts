// Phase 1 probe: what does TheCarApi actually return for Japanese R32 GT-R lots?
//
//   node scripts/probe-thecarapi.ts            # uses cached responses where present
//   node scripts/probe-thecarapi.ts --refresh  # re-fetches everything
//
// Live key, so every response is cached in probe-output/ (gitignored) and the
// script makes at most ~7 requests per fresh run.

import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const BASE = "https://api.thecarapi.com";
const OUT = join(import.meta.dirname, "..", "probe-output");
const REFRESH = process.argv.includes("--refresh");

if (existsSync(".env")) process.loadEnvFile(".env");
const KEY = process.env.THECARAPI_KEY;
if (!KEY) {
  console.error("THECARAPI_KEY is not set. Copy .env.example to .env and paste the key in.");
  process.exit(1);
}

mkdirSync(OUT, { recursive: true });
let requestsMade = 0;

async function get(name: string, path: string): Promise<any> {
  const file = join(OUT, `${name}.json`);
  if (!REFRESH && existsSync(file)) return JSON.parse(readFileSync(file, "utf8")).body;

  requestsMade++;
  const res = await fetch(BASE + path, { headers: { "X-API-Key": KEY!, Accept: "application/json" } });
  const headers = Object.fromEntries(
    ["x-request-id", "x-cache", "x-data-source", "x-ratelimit-limit", "x-ratelimit-remaining", "x-ratelimit-reset", "retry-after"]
      .map((h) => [h, res.headers.get(h)])
      .filter(([, v]) => v !== null),
  );
  const text = await res.text();
  let body: any;
  try { body = JSON.parse(text); } catch { body = text; }
  console.log(`  ${res.status} GET ${path}  ${JSON.stringify(headers)}`);
  if (!res.ok) {
    console.error(`  ! ${typeof body === "string" ? body.slice(0, 300) : JSON.stringify(body).slice(0, 300)}`);
    return null;
  }
  writeFileSync(file, JSON.stringify({ path, status: res.status, headers, body }, null, 2));
  return body;
}

// Flatten an object into dotted key paths -> sample value, so we can see what's populated.
function inventory(rows: any[]): Map<string, { filled: number; sample: unknown }> {
  const inv = new Map<string, { filled: number; sample: unknown }>();
  const walk = (v: any, path: string) => {
    if (v && typeof v === "object" && !Array.isArray(v)) {
      for (const [k, child] of Object.entries(v)) walk(child, path ? `${path}.${k}` : k);
      return;
    }
    const entry = inv.get(path) ?? { filled: 0, sample: undefined };
    const empty = v === null || v === undefined || v === "" || (Array.isArray(v) && v.length === 0);
    if (!empty) {
      entry.filled++;
      if (entry.sample === undefined) entry.sample = Array.isArray(v) ? `[${v.length}] ${JSON.stringify(v[0]).slice(0, 80)}` : v;
    }
    inv.set(path, entry);
  };
  for (const r of rows) walk(r, "");
  return inv;
}

function printInventory(title: string, rows: any[]) {
  console.log(`\n=== ${title} (${rows.length} rows) ===`);
  for (const [path, { filled, sample }] of [...inventory(rows)].sort()) {
    const s = sample === undefined ? "" : String(typeof sample === "string" ? sample : JSON.stringify(sample)).slice(0, 90);
    console.log(`  ${`${filled}/${rows.length}`.padStart(7)}  ${path.padEnd(48)} ${s}`);
  }
}

const rowsOf = (body: any): any[] => {
  if (!body || typeof body !== "object") return [];
  for (const k of ["results", "data", "items", "auctions", "rows", "vehicles"]) if (Array.isArray(body[k])) return body[k];
  return Array.isArray(body) ? body : [];
};

// R32 GT-R: BNR32, built 1989-1994.
const R32 = "brand=nissan&site=japan&production_year_from=1989&production_year_to=1995";

console.log("1) Sources visible to this key");
const sites = await get("01-sites", "/api/sites");
console.log(JSON.stringify(sites, null, 1)?.slice(0, 1200));

console.log("\n2) Nissan models in the Japan feed (to learn how 'Skyline'/'GT-R' is spelled)");
const models = await get("02-models-nissan-japan", "/api/models?brand=nissan&site=japan");
console.log(JSON.stringify(models)?.slice(0, 1500));

console.log("\n3) Live Japan lots: Nissan Skyline, built 1989-1995");
const live = await get("03-search-live", `/api/search?${R32}&model=skyline&limit=50`);
const liveRows = rowsOf(live);
console.log(`  total=${live?.total ?? "?"} rows=${liveRows.length}`);
for (const r of liveRows.slice(0, 15))
  console.log(`  - ${r.auction_id_str} | ${r.car_name_en ?? r.model_display} | ${r.production_year ?? r.registration_year} | ${r.mileage} km | cur=${r.current_price} final=${r.final_price} eur=${r.public_price_eur} | ends ${r.auction_end_at}`);
printInventory("search row fields", liveRows);

const gtr = liveRows.find((r) => /^BNR32/.test(r.model_code ?? "")) ?? liveRows[0];
if (gtr) {
  const id = gtr.auction_id_str ?? String(gtr.auction_id);
  console.log(`\n4) Full detail for ${id} (${gtr.car_name_en})`);
  const detail = await get("04-detail", `/api/auction/japan/${id}`);
  printInventory("detail fields", detail ? [detail] : []);

  console.log(`\n5) Price history for ${id}`);
  const hist = await get("05-price-history", `/api/auction/japan/${id}/price-history`);
  console.log(JSON.stringify(hist)?.slice(0, 1500));
} else {
  console.log("\n(no live rows — skipping detail and price history)");
}

console.log("\n6) Archived (ended) Japan Skyline lots — candidate price-history source");
const arch = await get("06-archive", `/api/archive/search?brand=nissan&model=skyline&site=japan&production_year_from=1989&production_year_to=1995&limit=50`);
const archRows = rowsOf(arch);
console.log(`  total=${arch?.total ?? "?"} rows=${archRows.length}`);
printInventory("archive row fields", archRows);

console.log("\n7) Archive coverage per source");
const stats = await get("07-archive-stats", "/api/archive/stats");
console.log(JSON.stringify(stats, null, 1)?.slice(0, 2500));

console.log(`\nDone. Requests made this run: ${requestsMade}. Raw responses in probe-output/.`);
