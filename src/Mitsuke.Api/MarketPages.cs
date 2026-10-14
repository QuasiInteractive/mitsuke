using Microsoft.Extensions.Caching.Memory;
using Mitsuke.Core;

namespace Mitsuke.Api;

/// <summary>A model on the /market index: what it is, what it usually opens at, how many are at auction now.</summary>
public sealed record MarketModelCard(string Make, string Model, string Code, string Name, int? YearFrom, int? YearTo, int Cars, Money Typical,
    int OnAuctionNow, DateOnly Since);

/// <summary>Everything one model's market page shows.</summary>
public sealed record MarketModelPage(MarketModelCard Model, IReadOnlyList<MarketMonth> Months, MarketMeter? Meter, IReadOnlyList<LotCard> Live,
    string Basis);

/// <summary>
/// The market pages: built from Mitsuke's own opening-bid history (one row per car), cached because they're the same
/// for everyone and the queries scan the whole history.
/// </summary>
public sealed class MarketPagesBuilder(IMarketStore market, IReadQueries queries, LotViewBuilder lots, IMemoryCache cache, TimeProvider clock)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan History = TimeSpan.FromDays(3 * 365);
    public const int MinimumCars = 5;

    public const string Basis = "Opening bids at Japanese auctions seen by Mitsuke, one per car. Opening bids are a floor, " +
        "not sale prices: cars usually sell for more.";

    public async Task<IReadOnlyList<MarketModelCard>> ModelsAsync(CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync("market:models", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            var now = clock.GetUtcNow();
            return (await market.ListModelsAsync(now - History, now, MinimumCars, cancellationToken)).Select(Card).ToList();
        }))!;

    /// <summary>Every model seen at all: a page still renders for one with too little history to be listed (a lot links to it).</summary>
    private async Task<IReadOnlyList<MarketModelCard>> AnyModelsAsync(CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync("market:any", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            var now = clock.GetUtcNow();
            return (await market.ListModelsAsync(now - History, now, 1, cancellationToken)).Select(Card).ToList();
        }))!;

    public async Task<MarketModelPage?> PageAsync(string make, string code, string destination, CancellationToken cancellationToken)
    {
        var model = (await AnyModelsAsync(cancellationToken))
            .FirstOrDefault(m => m.Make.Equals(make, StringComparison.OrdinalIgnoreCase) && m.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (model is null) return null;

        return await cache.GetOrCreateAsync($"market:page:{model.Make}:{model.Code}:{destination}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            var now = clock.GetUtcNow();
            var scope = new MarketScope(model.Make, model.Model, [model.Code]);
            var months = MarketHistories.Monthly(await market.GetSamplesAsync(scope, now - History, now, cancellationToken));
            var meter = await lots.MarketAsync(scope, null, cancellationToken);

            var watch = new Watchlist { Id = Guid.Empty, Name = model.Name, Make = model.Make, Model = model.Model, ModelCodes = new HashSet<string> { model.Code } };
            var current = await queries.GetCurrentLotsAsync(watch, now, 12, cancellationToken);
            var live = await lots.CardsAsync(current.Select(id => new MatchSummary(id, Guid.Empty, model.Name, now)), destination, cancellationToken);

            return new MarketModelPage(model, months, meter, live, Basis);
        });
    }

    private static MarketModelCard Card(MarketModel m)
    {
        var known = KnownGenerations.Find(m.Make, m.Code);
        var name = known?.Label ?? $"{KnownGenerations.Label(m.Model)} {m.Code}";
        return new MarketModelCard(m.Make, m.Model, m.Code, name, known?.From, known?.To, m.Cars,
            new Money(Math.Round(m.Typical / 10_000m) * 10_000m, "JPY"), m.OnAuctionNow, m.Since);
    }
}
