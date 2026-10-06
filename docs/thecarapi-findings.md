# TheCarApi — Japan source findings (Phase 1 probe, 2026-10-06)

Probe: `scripts/probe-thecarapi.ts`. 8 requests in total, with responses cached in `probe-output/` (gitignored).
Query: `site=japan&brand=nissan&model=skyline&production_year_from=1989&production_year_to=1995`.
The free-text `search=skyline` returned 0 results, so use `model=` instead.

## Volume
- `japan` has about 50.6k live lots. The query found **30 live Skylines (1989–95), almost all BNR32**, and 276 archived ones (27 of the first 50 were BNR32).
- The archive only goes back to **2026-09-24** for japan (324k rows). Most of the history we'll need, we have to build ourselves.

## What's there (search row, every row unless noted)
| Need | Field | Notes |
|---|---|---|
| ID | `auction_id_str` | Keep it as a string. |
| Auction house | `auction_house` | For example `uss_tokyo`, `uss_nagoya_hokuriku`, `bayauc`, `ju_kanagawa`. |
| Lot / date | `lot_number`, `auction_end_at`, `batch_end_date` | |
| Model code | `model_code` | `BNR32` is the clean GT-R filter. There are also variants like `BNR32カイ` (modified). |
| Grade | `auction_grade` | `3`, `3.5`, `4`, `4.5`, `R`, `RA`, `***`. Interior grade is detail-only. |
| Mileage / year | `mileage`, `production_year`, `registration_year` | `production_month` is empty. |
| Frame no. | `frame_number` | About 60%. **VIN is never present** (JDM cars don't have one). The frame number works as the stable identity. |
| Price | `native_prices.current_price` (JPY) | About 97%. This is the **opening bid**. |
| Photos | `images[]` (max 8), `picture_count` | Live rows have 6. Archive rows mostly have 1 (thumbnail only). |

## Detail-only (`/api/auction/japan/{id}`), `auction.car_identification`
- **`InspectionReports[]`**: the **auction sheet image** (`type: auction_sheet`) **plus sheets from previous auctions** (`previous_auction_sheet`, with dates). These go straight into the Kensa-ya decoder.
- **`SheetOcr`**: their own OCR of the sheet (model code, colour, damage codes, interior grade, shift, recycle fee, A/C). It's noisy: `car_name` is garbled, and first_registration read as 2020-08 on a 1990 car. Treat it as a hint and don't rely on it.
- **`JapanRelists[]`**: every previous time this car went to auction, with date, lot, venue, grade and opening price. The sample car had been relisted 5 times in 5 weeks at the same ¥3.98M, so it's clearly not selling at that price. This is a strong deal-score signal.
- **`JapanMerged.PriceEvidence` / `JapanOffers`**: which resellers (e.g. karasaki, upcars) reported the price, and their basis (`"unknown"`, `"meaning unverified"`).
- `eur_rates`: their FX table, which includes AUD and NZD. We should still run our own FX job.

## Gaps and traps
- **No hammer/sold prices.** `archive/stats` says japan has `with_final_price: 0`. Archive rows show `final_price` filled in, but it's just a copy of the opening bid. Only about 3 of 50 rows had a native `final_price`, and it's labelled unverified. **"Bottom 10% for this spec" has to be measured against opening bids and asking prices, not sale prices.**
- **`public_price_eur` ≠ the auction price.** On live lots it's about 1.5× the converted opening bid (it looks like it includes a reseller markup); on archive rows it equals the converted bid. **Ignore it and use the native JPY amount.**
- `/api/auction/{id}/price-history` only tracks EUR re-conversions of the same JPY figure (FX drift). It isn't useful for Japan.
- `details_pending: true` and `vault_gallery.pending: 6`: photos are fetched on demand, so the first detail call may not have them yet.
- Image and sheet URLs are **relative** (`/auction-photo/...`, `/report-vault/...`). They're relative to the TheCarApi host, and we still need to confirm whether they require the API key.
- The same car shows up again under a **new `auction_id`** each week (for example frame `BNR32-305737` on both 08-06 and 08-20). Dedupe on frame number, or on `model_code + mileage + grade + colour` when the frame number is missing.
- `fuel_group` (40%) and `gearbox_group` (23%) are sparse in search rows.
- No rate-limit headers were returned, so our key has no configured quota. Self-limiting still applies.

## Design implications
1. Store a `listing` keyed by `(source, source_id)`, plus a separate **`vehicle`** identity (frame number, or a fuzzy key) so relists link together.
2. Store **`price_observation`** rows per (listing, observed_at, kind = opening/current/reported_final, JPY amount). This builds our own history from now on.
3. Fetch detail **only for watchlist matches**, not for every row (it's the only place the sheets and relists are, and it's one request per car).
4. Attribution string: `"{auction_house} auction via TheCarApi"`, e.g. "USS Tokyo auction via TheCarApi".
