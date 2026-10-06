# Mitsuke 見つけ

A 24/7 car watchlist for AU/NZ buyers. Save the car you want ("R32 GT-R, grade 3.5+, under 150,000 km") and Mitsuke watches Japanese auctions, then alerts you with the facts, an estimated landed cost and a link to the full report.

> Sister product to [Kensa-ya](https://kensa-ya.vercel.app): **Mitsuke finds the car → Kensa-ya checks the car.**

## Status

| Piece | State |
|---|---|
| TheCarApi source adapter (Japan auctions) | ✅ Live, tested |
| Normalised `Listing` model, watchlist matching | ✅ |
| Alert text + console/Discord delivery | ✅ |
| Postgres (listings, vehicles, price history, watchlists) | Next |
| Queue-based pipeline on Azure Functions | Planned |
| Landed cost (AUD/NZD), deal score, Kensa-ya sheet decoding | Planned |
| Bicep IaC, CI/CD, Key Vault, Application Insights | Planned |

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
- **Honest data.** Japanese auction prices are opening bids, never sale prices, and `PriceKind` carries that through to the alert text. Unknown values fail filters that need them, so Mitsuke never alerts on a guess. Every alert names its source and carries a disclaimer.

Why things are the way they are: [docs/thecarapi-findings.md](docs/thecarapi-findings.md) covers what the data actually contains, verified against the live API before the schema was designed.

## Projects

| Project | Purpose |
|---|---|
| `src/Mitsuke.Core` | Domain: `Listing`, `Watchlist`, `WatchlistMatcher`, `AlertFormatter`, source/notifier interfaces. No dependencies. |
| `src/Mitsuke.Sources.TheCarApi` | TheCarApi adapter: typed `HttpClient`, resilience pipeline, JSON → `Listing` mapping. |
| `src/Mitsuke.Cli` | Local runner: one scan of the pipeline. |
| `tests/Mitsuke.Tests` | xUnit. Hand-written fixtures (no copied API data; the provider's terms forbid redistributing raw feeds). |

## Run it

Needs the .NET 10 SDK.

```bash
cp .env.example .env     # then put your TheCarApi key in .env
dotnet test
dotnet run --project src/Mitsuke.Cli -- scan
```

Set `DISCORD_WEBHOOK_URL` in `.env` to get alerts in Discord instead of the console.

## Rules this project keeps

- No scraping of sites that forbid it, no fake accounts, no stored third-party passwords.
- Secrets only in environment variables (Key Vault when deployed). Never in code or git.
- Show analysed results, never raw feeds. Attribute every listing to its source.
