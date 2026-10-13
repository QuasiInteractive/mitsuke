using System.Globalization;

namespace Mitsuke.Core;

public enum BidRoom
{
    /// <summary>The budget doesn't even cover the car at its opening bid.</summary>
    BelowOpening,
    /// <summary>Only just above the opening bid: wins only if nobody else bids.</summary>
    Tight,
    Room,
}

/// <summary>The most to bid in Japan so the car lands within a budget at home, and how that compares with the opening bid.</summary>
public sealed record MaxBidResult
{
    public required Money Budget { get; init; }

    /// <summary>Highest bid, in ¥10,000 steps, whose estimated landed total fits the budget. Null if not even ¥0 fits.</summary>
    public Money? MaxBid { get; init; }
    public LandedEstimate? LandedAtMax { get; init; }
    public Money? OpeningBid { get; init; }

    /// <summary>How far the max bid is above the opening bid: 12 means 12% above.</summary>
    public int? HeadroomPercent { get; init; }
    public required BidRoom Room { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// The landed-cost engine run backwards. Landed cost rises with the bid (duty, GST and luxury car tax are all on the
/// price), so the largest bid that fits is found by binary search over ¥10,000 steps, the way auctions step bids.
/// </summary>
public static class MaxBidCalculator
{
    public const decimal Step = 10_000m;
    private const decimal Ceiling = 200_000_000m;

    /// <summary>Under this much headroom over the opening bid, the max is "tight".</summary>
    public const int TightPercent = 5;

    public static async Task<MaxBidResult?> FindAsync(ILandedCostEstimator landedCost, Money budget, string destination, Money? openingBid,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(landedCost);
        async Task<LandedEstimate?> At(decimal steps) => await landedCost.EstimateAsync(new Money(steps * Step, "JPY"), destination, cancellationToken);

        // Only a budget in the destination's own currency means anything ("A$45,000 on the ground in Australia").
        if (await At(0) is not { } floor || floor.Total.Currency != budget.Currency) return null;

        LandedEstimate? best = null;
        decimal lo = 0, hi = Ceiling / Step;
        if (floor.Total.Amount > budget.Amount) lo = -1;
        else
        {
            best = floor;
            while (lo < hi)
            {
                var mid = Math.Ceiling((lo + hi) / 2);
                var estimate = await At(mid);
                if (estimate is not null && estimate.Total.Amount <= budget.Amount) { lo = mid; best = estimate; }
                else hi = mid - 1;
            }
        }

        Money? max = lo < 0 ? null : new Money(lo * Step, "JPY");
        int? headroom = max is { } m && openingBid is { Currency: "JPY", Amount: > 0 } o
            ? (int)Math.Floor(100m * (m.Amount - o.Amount) / o.Amount)
            : null;
        var room = max is null || (openingBid is { Currency: "JPY" } ob && max.Value.Amount < ob.Amount) ? BidRoom.BelowOpening
            : headroom is { } h && h < TightPercent ? BidRoom.Tight
            : BidRoom.Room;

        return new MaxBidResult
        {
            Budget = budget,
            MaxBid = max,
            LandedAtMax = lo < 0 ? null : best,
            OpeningBid = openingBid,
            HeadroomPercent = headroom,
            Room = room,
            Summary = Summarise(room, max, headroom, openingBid),
        };
    }

    private static string Summarise(BidRoom room, Money? max, int? headroom, Money? opening)
    {
        var en = CultureInfo.GetCultureInfo("en-AU");
        string Yen(Money m) => string.Create(en, $"¥{m.Amount:N0}");
        return room switch
        {
            BidRoom.BelowOpening when max is { } m && opening is { } o =>
                $"Your budget covers a bid of {Yen(m)}, under the {Yen(o)} opening bid: this car is out of reach unless it's relisted cheaper.",
            BidRoom.BelowOpening => "Your budget doesn't cover the fees, shipping and taxes on their own.",
            BidRoom.Tight => $"Only {headroom}% over the opening bid: it would take nobody else bidding. That does happen, mostly to cars that keep coming back.",
            _ when headroom is { } h => $"Bid up to {Yen(max!.Value)}, {h}% over the opening bid. Set it with your exporter and don't chase it on the day.",
            _ => $"Bid up to {Yen(max!.Value)}. Set it with your exporter and don't chase it on the day.",
        };
    }
}
