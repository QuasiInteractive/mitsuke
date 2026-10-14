namespace Mitsuke.Core;

/// <summary>A month of one model's market: typical (median) opening bid, the usual range, the extremes, how many cars.</summary>
public sealed record MarketMonth(DateOnly Month, decimal Typical, decimal Low, decimal High, decimal Min, decimal Max, int Cars);

/// <summary>A model with enough history to have a market page: the index of /market.</summary>
public sealed record MarketModel(string Make, string Model, string Code, int Cars, decimal Typical, int OnAuctionNow, DateOnly Since);

public static class MarketHistories
{
    /// <summary>Months need this many cars before they're charted: fewer is an anecdote, not a market.</summary>
    public const int CarsPerMonth = 3;

    /// <summary>Opening bids grouped by calendar month (Japan), oldest first, thin months left out.</summary>
    public static IReadOnlyList<MarketMonth> Monthly(IEnumerable<MarketSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return samples
            .Where(s => s.Amount > 0)
            .GroupBy(s => new DateOnly(s.Seen.Year, s.Seen.Month, 1))
            .Where(g => g.Count() >= CarsPerMonth)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var a = g.Select(s => s.Amount).Order().ToList();
                var mid = a.Count / 2;
                var median = a.Count % 2 == 1 ? a[mid] : (a[mid - 1] + a[mid]) / 2;
                return new MarketMonth(g.Key, median, DealScorer.Percentile(a, 0.1m), DealScorer.Percentile(a, 0.9m), a[0], a[^1], a.Count);
            })
            .ToList();
    }
}
