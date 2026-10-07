# Mitsuke 見つけ

[![CI](https://github.com/QuasiInteractive/mitsuke/actions/workflows/ci.yml/badge.svg)](https://github.com/QuasiInteractive/mitsuke/actions/workflows/ci.yml)

A 24/7 car watchlist for AU/NZ buyers. Save the car you want ("R32 GT-R, grade 3.5+, under 150,000 km") and Mitsuke watches Japanese auctions, then alerts you with the facts, an estimated landed cost and a link to the full report.

> Sister product to [Kensa-ya](https://kensa-ya.vercel.app): **Mitsuke finds the car → Kensa-ya checks the car.**

## Status

| Piece | State |
|---|---|
| TheCarApi source adapter (Japan auctions) | ✅ Live, tested |
| Normalised `Listing` model, watchlist matching | ✅ |
| Alert text + console/Discord delivery | ✅ |
| Postgres: listings, vehicles (relist linking), price history, watchlists, alert log | ✅ Migrations + integration tests |
| Alert-once guarantee (safe to poll 24/7, failed sends retried) | ✅ |
| CI: build + all tests incl. Postgres on every push (GitHub Actions) | ✅ |
| Lot details for matches: auction sheet, earlier auction appearances, full gallery | ✅ |
| Landed cost AU/NZ/US with live exchange rates, "under A$45K landed" watchlists | ✅ Identical to Kensa-ya, proven by 160 golden cases |
| Deal score: percentile vs comparable cars, one per physical car, with confidence | ✅ |
| Runs 24/7 on Azure Functions: timer → `collect` queue → `alerts` queue, retries + poison (dead-letter) queues, health endpoint, OpenTelemetry | ✅ Runs locally on Azurite |
| Web app: matches + lot page (gallery, landed-cost breakdown, deal score, decoded sheet on a car diagram, countdown, "I want to bid") | ✅ Next.js 16 + Mitsuke.Api |
| Accounts: magic-link sign-in (Supabase Auth), your own watchlists with presets, keep watching / not for me | ✅ |
| Per-person email alerts: HTML card (photo, landed cost, deal score, red flags, lot link), every value HTML-encoded; demo lists stay on Discord | ✅ (SMTP: Gmail in production, Mailpit locally) |
| Phone push notifications | Next |

| Auction sheet decoded into plain English via Kensa-ya's partner API; serious red flags lead the alert | ✅ |
| Live: [mitsuke-jp.vercel.app](https://mitsuke-jp.vercel.app) on Vercel + Azure (Functions Flex, App Service F1, Key Vault, App Insights) + Supabase, all from Bicep | ✅ |
| Continuous deployment: push to main → tests → migrate → deploy API + pipeline → smoke test (GitHub Actions, OIDC, no stored secrets); web via Vercel Git | ✅ |

## Architecture

```
 every 10 min            "collect" queue                         "alerts" queue
┌──────────────────┐   ┌──────────────────────────────────┐   ┌───────────────────────────────────┐
│ ScheduleCollection│──▶│ Collect (one per watchlist)       │──▶│ SendAlert (one per match)          │──▶ Discord
│ (timer trigger)   │   │ search sources → upsert listings  │   │ claim once → lot details → landed  │    email / push
└──────────────────┘   │ → landed cost → match             │   │ cost → deal score → format → send  │    (later)
                       └──────────────────────────────────┘   └───────────────────────────────────┘
                          5 tries, then collect-poison            5 tries, then alerts-poison

                Postgres: listings · vehicles · price_observations · listing_details · watchlists · alerts
```

The same `Collector` and `AlertSender` run in-process for `dotnet run -- scan`, so the CLI and the cloud share one code path.

- **Swappable sources.** Every provider implements `IListingSource` and emits the same `Listing`. Replacing a provider touches one project.
- **Resilience on every external call.** The TheCarApi client runs through a Polly pipeline: total timeout → retry with jittered backoff (honours `Retry-After`) → circuit breaker → client-side rate limit → per-attempt timeout. See [`ServiceCollectionExtensions.cs`](src/Mitsuke.Sources.TheCarApi/ServiceCollectionExtensions.cs).
- **Polite by construction.** A hard page cap per search means a bad filter can never become a bulk crawl, and a sliding-window limiter caps requests per minute even though the key has no server quota.
- **At-least-once queues, exactly-once alerts.** Storage queues may redeliver, so every stage is safe to repeat: collecting is upserts, and sending is guarded by a claim in Postgres. A claim left `pending` by a sender that died mid-send expires after 10 minutes and is retried; nothing is ever sent twice. Messages that keep failing land in `*-poison` queues for inspection.
- **Idempotent by design.** Re-running a scan never double-alerts: each (watchlist, listing, channel) alert is claimed in Postgres before sending (`INSERT ... ON CONFLICT`), marked sent or failed afterwards, and only failed ones are retried.
- **Our own price history.** TheCarApi has no Japanese sale prices, so every price *change* is logged per listing, and relisted cars are linked to one `vehicle` by frame number.
- **One engine, two products.** Landed cost is Kensa-ya's engine, vendored unchanged by [`scripts/sync-kensaya-engine.sh`](scripts/sync-kensaya-engine.sh) together with Kensa-ya's golden answers; [`LandedCostParityTests`](tests/Mitsuke.Tests/LandedCostParityTests.cs) fails the build if Mitsuke would quote a different number, to the cent.
- **A deal score that shows its working.** Each match is ranked against comparable cars (same model code, similar year, mileage and grade; relists collapsed to one car via the `vehicles` table; never compared with itself). It reports how many cars it used and a confidence, and says nothing rather than guess when there are fewer than five. `backfill` seeds comparables from TheCarApi's archive.
- **Shared brains, private secrets.** Sheet reading is Kensa-ya's paid product, so it is *called*, never copied: Mitsuke posts a sheet URL to Kensa-ya's partner endpoint (bearer key, host allowlist against SSRF) and maps the answer. It runs only for alerts actually going out, is cached per listing (each read is a paid AI call, about US$0.05–0.09), and sits behind its own retry/circuit-breaker pipeline; if Kensa-ya is down, alerts still go out without it.
- **Auth done the boring, correct way.** Sign-in is Supabase Auth (magic links, no passwords stored by Mitsuke). The API validates its ES256 tokens with stock ASP.NET Core JWT bearer auth, discovering the signing keys from the issuer (no shared secret, rotation needs no deploy). Every `/api/me` query is scoped by the token's subject, and the tests prove a forged, mis-issued, wrong-audience or expired token gets a 401 and that one person can't touch another's watchlists.
- **Deploys you can trust.** Every push to `main` runs the tests, then (only if they pass) migrates the database, ships the API and the pipeline, and smoke-tests production. GitHub signs in to Azure with OIDC: a deploy identity trusted only for this repo's `production` environment, allowed to deploy to one resource group and read one Key Vault. No Azure credentials are stored anywhere.
- **Honest data.** Japanese auction prices are opening bids, never sale prices, and `PriceKind` carries that through to the alert text. Unknown values fail filters that need them, so Mitsuke never alerts on a guess. Every alert names its source and carries a disclaimer.

Why things are the way they are: [docs/thecarapi-findings.md](docs/thecarapi-findings.md) covers what the data actually contains, verified against the live API before the schema was designed.

## Projects

| Project | Purpose |
|---|---|
| `src/Mitsuke.Core` | Domain + the pipeline (`Scanner`): `Listing`, `Watchlist`, `WatchlistMatcher`, `AlertFormatter`, source/storage/notifier interfaces. No infrastructure dependencies. |
| `src/Mitsuke.Sources.TheCarApi` | TheCarApi adapter: typed `HttpClient`, resilience pipeline, JSON → `Listing` mapping. |
| `src/Mitsuke.Pricing` | Landed-cost estimates: Kensa-ya's engine and country rules (in `Kensaya/` and `data/`, synced, not edited) behind Mitsuke's `ILandedCostEstimator`, with live ECB exchange rates. |
| `src/Mitsuke.Data` | Postgres via Npgsql + Dapper. Forward-only SQL migrations in [`Migrations/`](src/Mitsuke.Data/Migrations), applied under an advisory lock. |
| `src/Mitsuke.Functions` | Azure Functions (isolated, .NET 10): the timer + two queue-triggered stages, `GET /api/health`, OpenTelemetry to Application Insights. |
| `src/Mitsuke.Kensaya` | Client for Kensa-ya's partner API (sheet decoding), with its own resilience pipeline. |
| `src/Mitsuke.Notifications` | Delivery channels behind `INotifier` (Discord webhook now; email and push next). |
| `src/Mitsuke.Api` | ASP.NET Core minimal API for the web app: lot views, matches, watchlists, bid requests (validated, rate-limited). OpenAPI at `/openapi/v1.json` in development. |
| `web/` | Next.js 16 front end (React 19, Tailwind 4, Cache Components / partial prerendering). Calls the API from its server only. |
| `src/Mitsuke.Cli` | Local runner: `migrate`, `seed`, `scan`. |
| `tests/Mitsuke.Tests` | xUnit unit tests, plus integration tests against a real Postgres via Testcontainers. Hand-written fixtures (no copied API data; the provider's terms forbid redistributing raw feeds). |

## Run it

Needs the .NET 10 SDK and Docker.

```bash
cp .env.example .env     # then fill in the TheCarApi key and a local database password
docker compose up -d     # Postgres 17 on localhost:5432
dotnet test              # unit + integration tests (starts its own throwaway Postgres)

dotnet run --project src/Mitsuke.Cli -- migrate
dotnet run --project src/Mitsuke.Cli -- seed
dotnet run --project src/Mitsuke.Cli -- backfill  # one-off: past lots for the deal score
dotnet run --project src/Mitsuke.Cli -- scan    # run it twice: the second pass sends nothing new
```

Set `DISCORD_WEBHOOK_URL` in `.env` to get alerts in Discord instead of the console.

### Run the web app

```bash
npx supabase start                                                   # local Supabase Auth (ports 643xx); emails land in Mailpit at :64324
dotnet run --project src/Mitsuke.Api --urls http://localhost:5107   # the API (reads .env; set SUPABASE_URL=http://127.0.0.1:64321)
cd web && cp .env.example .env.local && npm install && npm run dev  # http://localhost:3001 (publishable key from `npx supabase status`)
```

Supabase's default ports (543xx) collide with Windows' reserved port ranges, hence 643xx in `supabase/config.toml`.

Alerts link to their lot page when `MITSUKE_WEB_URL` is set in `.env`.

### Run it 24/7 locally (Azure Functions + Azurite)

Needs [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local) v4.

```bash
docker compose up -d                          # Postgres + Azurite (local Azure Storage)
python scripts/local-settings.py              # writes the gitignored local.settings.json from .env
cd src/Mitsuke.Functions && func start --port 7073
```

`python scripts/local-settings.py --schedule "0 */1 * * * *"` runs the collection every minute instead of every ten, to watch it work.

## Rules this project keeps

- No scraping of sites that forbid it, no fake accounts, no stored third-party passwords.
- Secrets only in environment variables (Key Vault when deployed). Never in code or git.
- Show analysed results, never raw feeds. Attribute every listing to its source.
