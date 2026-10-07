# Mitsuke 見つけ

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
| Queue-based pipeline on Azure Functions | Next |
| Landed cost (AUD/NZD), deal score, Kensa-ya sheet decoding | Planned |
| Bicep IaC, CD, Key Vault, Application Insights | Planned |

## Architecture

```
 collectors (timer)        queue          processors (queue-triggered)              notify
┌───────────────────┐   ┌───────┐   ┌──────────────────────────────────────┐   ┌──────────────┐
│ TheCarApi (Japan) │──▶│       │──▶│ normalise → dedupe → match → score   │──▶│ Discord      │
│ Gmail alert inbox │──▶│ lots  │   │                                      │   │ email / push │
│ Trade Me          │──▶│       │   │ Postgres: listings, vehicles, prices │   │ SMS (later)  │
└───────────────────┘   └───────┘   └──────────────────────────────────────┘   └──────────────┘
```

- **Swappable sources.** Every provider implements `IListingSource` and emits the same `Listing`. Replacing a provider touches one project.
- **Resilience on every external call.** The TheCarApi client runs through a Polly pipeline: total timeout → retry with jittered backoff (honours `Retry-After`) → circuit breaker → client-side rate limit → per-attempt timeout. See [`ServiceCollectionExtensions.cs`](src/Mitsuke.Sources.TheCarApi/ServiceCollectionExtensions.cs).
- **Polite by construction.** A hard page cap per search means a bad filter can never become a bulk crawl, and a sliding-window limiter caps requests per minute even though the key has no server quota.
- **Idempotent by design.** Re-running a scan never double-alerts: each (watchlist, listing, channel) alert is claimed in Postgres before sending (`INSERT ... ON CONFLICT`), marked sent or failed afterwards, and only failed ones are retried.
- **Our own price history.** TheCarApi has no Japanese sale prices, so every price *change* is logged per listing, and relisted cars are linked to one `vehicle` by frame number.
- **Honest data.** Japanese auction prices are opening bids, never sale prices, and `PriceKind` carries that through to the alert text. Unknown values fail filters that need them, so Mitsuke never alerts on a guess. Every alert names its source and carries a disclaimer.

Why things are the way they are: [docs/thecarapi-findings.md](docs/thecarapi-findings.md) covers what the data actually contains, verified against the live API before the schema was designed.

## Projects

| Project | Purpose |
|---|---|
| `src/Mitsuke.Core` | Domain + the pipeline (`Scanner`): `Listing`, `Watchlist`, `WatchlistMatcher`, `AlertFormatter`, source/storage/notifier interfaces. No infrastructure dependencies. |
| `src/Mitsuke.Sources.TheCarApi` | TheCarApi adapter: typed `HttpClient`, resilience pipeline, JSON → `Listing` mapping. |
| `src/Mitsuke.Data` | Postgres via Npgsql + Dapper. Forward-only SQL migrations in [`Migrations/`](src/Mitsuke.Data/Migrations), applied under an advisory lock. |
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
dotnet run --project src/Mitsuke.Cli -- scan    # run it twice: the second pass sends nothing new
```

Set `DISCORD_WEBHOOK_URL` in `.env` to get alerts in Discord instead of the console.

## Rules this project keeps

- No scraping of sites that forbid it, no fake accounts, no stored third-party passwords.
- Secrets only in environment variables (Key Vault when deployed). Never in code or git.
- Show analysed results, never raw feeds. Attribute every listing to its source.
