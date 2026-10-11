using System.Globalization;

namespace Mitsuke.Core;

/// <summary>One car's opening bid in one week it was at auction (relists count again in later weeks).</summary>
public sealed record MarketSample(DateOnly Seen, decimal Amount, bool OnAuctionNow);

/// <summary>A week of the market: the typical (median) opening bid and how many cars were offered.</summary>
public sealed record MarketWeek(DateOnly WeekOf, decimal Typical, int Cars);

public enum MarketSignal
{
    /// <summary>At least 10% under the typical opening bid.</summary>
    Below,
    About,
    /// <summary>At least 10% over it.</summary>
    Above,
}

/// <summary>
/// The "stock chart for cars": what a model's opening bids have looked like lately, week by week, and where one lot
/// sits in that. It's built from opening bids (Japan publishes no sale prices), so it says "cheap to start", not
/// "cheap to win": the meter switches to real results once a partner exporter reports them.
/// </summary>
public sealed record MarketMeter
{
    public required string Name { get; init; }
    public required string Currency { get; init; }

    /// <summary>Car-weeks behind the numbers (one car at auction in two weeks counts twice).</summary>
    public required int Cars { get; init; }
    public required decimal Typical { get; init; }

    /// <summary>10th to 90th percentile: where most opening bids land.</summary>
    public required decimal Low { get; init; }
    public required decimal High { get; init; }

    /// <summary>Every opening bid behind the meter, cheapest first (capped), for the dot strip.</summary>
    public required IReadOnlyList<decimal> Spread { get; init; }
    public required IReadOnlyList<MarketWeek> Weeks { get; init; }

    /// <summary>Change in the weekly typical from the first to the last full week; null until there's enough history.</summary>
    public decimal? TrendPercent { get; init; }
    public required int OnAuctionNow { get; init; }
    public required DateOnly Since { get; init; }

    /// <summary>The lot this meter was built for, when there is one.</summary>
    public decimal? This { get; init; }
    public MarketSignal? Signal { get; init; }

    /// <summary>This lot against typical: -23 means 23% under.</summary>
    public int? VsTypicalPercent { get; init; }

    public required string Summary { get; init; }
}

public static class MarketMeters
{
    public const int MinimumCars = 5;

    /// <summary>A week needs this many cars before its typical is plotted or trusted for a trend.</summary>
    public const int CarsPerWeek = 3;

    /// <summary>Weeks of history the trend needs, so a week or two of noise isn't sold as a trend.</summary>
    public const int TrendWeeks = 3;

    private const int SpreadCap = 80;

    /// <returns>Null when there are too few cars to say anything honest.</returns>
    public static MarketMeter? Build(string name, string currency, IReadOnlyList<MarketSample> samples, decimal? thisPrice = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var priced = samples.Where(s => s.Amount > 0).ToList();
        if (priced.Count < MinimumCars) return null;

        var amounts = priced.Select(s => s.Amount).Order().ToList();
        var typical = Median(amounts);

        var weeks = priced
            .GroupBy(s => WeekOf(s.Seen))
            .OrderBy(g => g.Key)
            .Select(g => new MarketWeek(g.Key, Median(g.Select(s => s.Amount).Order().ToList()), g.Count()))
            .ToList();
        var solid = weeks.Where(w => w.Cars >= CarsPerWeek).ToList();
        decimal? trend = solid.Count >= TrendWeeks && solid[0].Typical > 0
            ? Math.Round(100m * (solid[^1].Typical - solid[0].Typical) / solid[0].Typical, 1)
            : null;

        int? vs = thisPrice is { } p && typical > 0 ? (int)Math.Round(100m * (p - typical) / typical, MidpointRounding.AwayFromZero) : null;
        MarketSignal? signal = vs switch
        {
            null => null,
            <= -10 => MarketSignal.Below,
            >= 10 => MarketSignal.Above,
            _ => MarketSignal.About,
        };

        return new MarketMeter
        {
            Name = name,
            Currency = currency,
            Cars = priced.Count,
            Typical = typical,
            Low = DealScorer.Percentile(amounts, 0.1m),
            High = DealScorer.Percentile(amounts, 0.9m),
            Spread = Thin(amounts, SpreadCap),
            Weeks = weeks,
            TrendPercent = trend,
            OnAuctionNow = priced.Count(s => s.OnAuctionNow),
            Since = priced.Min(s => s.Seen),
            This = thisPrice,
            Signal = signal,
            VsTypicalPercent = vs,
            Summary = Summarise(name, signal, vs, trend, solid.Count),
        };
    }

    /// <summary>Monday of the week: the market moves weekly, with the big auction days midweek.</summary>
    public static DateOnly WeekOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private static string Summarise(string name, MarketSignal? signal, int? vs, decimal? trend, int weeks)
    {
        var where = signal switch
        {
            MarketSignal.Below => string.Create(CultureInfo.InvariantCulture, $"Below usual: {-vs!.Value}% under the typical opening bid for a {name}."),
            MarketSignal.Above => string.Create(CultureInfo.InvariantCulture, $"Above usual: {vs!.Value}% over the typical opening bid for a {name}."),
            MarketSignal.About => $"About usual for a {name}.",
            _ => $"What {name}s have opened at lately.",
        };
        var moving = trend switch
        {
            null => " Trend shows once there are a few weeks of history.",
            > 2 => string.Create(CultureInfo.InvariantCulture, $" Opening bids up {trend:0.#}% over {weeks} weeks."),
            < -2 => string.Create(CultureInfo.InvariantCulture, $" Opening bids down {-trend:0.#}% over {weeks} weeks."),
            _ => string.Create(CultureInfo.InvariantCulture, $" Steady over {weeks} weeks."),
        };
        return where + moving;
    }

    /// <summary>Evenly spaced picks from a sorted list, always keeping both ends.</summary>
    private static List<decimal> Thin(List<decimal> sorted, int cap)
    {
        if (sorted.Count <= cap) return sorted;
        return Enumerable.Range(0, cap).Select(i => sorted[(int)Math.Round(i * (sorted.Count - 1) / (double)(cap - 1))]).ToList();
    }

    private static decimal Median(List<decimal> sorted)
    {
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}

/// <summary>A lot the nightly-ish market scan thinks is unusually cheap to start, with what it's being compared to.</summary>
public sealed record MarketCandidate(Guid ListingId, string Group, decimal Amount, decimal Typical, int Cars, DateTimeOffset FirstSeenAt);

/// <summary>Which cars a market meter or scan covers: a make plus chassis codes, or the model when there are none.</summary>
public sealed record MarketScope(string Make, string Model, IReadOnlyList<string> ModelCodes, int? YearFrom = null, int? YearTo = null)
{
    public static MarketScope For(Listing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);
        return new(listing.Make, listing.Model, listing.ModelCode is { } c ? [c] : [],
            listing.Year - 3, listing.Year + 3);
    }

    public static MarketScope For(Watchlist watchlist)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        return new(watchlist.Make, watchlist.Model, [.. watchlist.ModelCodes], watchlist.YearFrom, watchlist.YearTo);
    }

    public string Name => ModelCodes.Count > 0 ? string.Join("/", ModelCodes) : $"{Make} {Model}";
}

public interface IMarketStore
{
    /// <summary>One sample per car per week since <paramref name="since"/>, yen opening bids only.</summary>
    Task<IReadOnlyList<MarketSample>> GetSamplesAsync(MarketScope scope, DateTimeOffset since, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lots first seen, and going to auction, since <paramref name="since"/> whose opening bid is well under the typical for their chassis code
    /// (over the last twelve weeks), cleanest first: good grade, not repaired, not high mileage for the model.
    /// </summary>
    Task<IReadOnlyList<MarketCandidate>> ScanAsync(DateTimeOffset since, DateTimeOffset now, int limit, CancellationToken cancellationToken = default);
}
