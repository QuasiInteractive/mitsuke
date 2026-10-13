using Mitsuke.Core;

namespace Mitsuke.Tests;

public class MaxBidTests
{
    /// <summary>A$8,000 of fixed costs plus the car at ¥100 = A$1, with 10% on top: simple enough to check by hand.</summary>
    private sealed class Linear : ILandedCostEstimator
    {
        public Task<LandedEstimate?> EstimateAsync(Money price, string destination, CancellationToken cancellationToken = default)
        {
            var total = new Money(8_000m + price.Amount / 100m * 1.1m, "AUD");
            return Task.FromResult<LandedEstimate?>(new LandedEstimate { Destination = destination, Total = total, Low = total, High = total, JpyPerUnit = 100m });
        }
    }

    [Fact]
    public async Task Finds_the_highest_bid_in_10000_yen_steps_that_lands_within_budget()
    {
        // A$45,000 leaves A$37,000 for the car: ¥3,363,636, so ¥3,360,000 in auction steps.
        var r = (await MaxBidCalculator.FindAsync(new Linear(), new Money(45_000m, "AUD"), "AU", new Money(3_000_000m, "JPY")))!;

        Assert.Equal(new Money(3_360_000m, "JPY"), r.MaxBid);
        Assert.True(r.LandedAtMax!.Total.Amount <= 45_000m);
        Assert.Equal(12, r.HeadroomPercent);
        Assert.Equal(BidRoom.Room, r.Room);
        Assert.Contains("¥3,360,000", r.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Says_when_the_budget_is_tight_or_out_of_reach()
    {
        var tight = (await MaxBidCalculator.FindAsync(new Linear(), new Money(41_500m, "AUD"), "AU", new Money(3_000_000m, "JPY")))!;
        Assert.Equal(BidRoom.Tight, tight.Room);

        var under = (await MaxBidCalculator.FindAsync(new Linear(), new Money(30_000m, "AUD"), "AU", new Money(3_000_000m, "JPY")))!;
        Assert.Equal(BidRoom.BelowOpening, under.Room);
        Assert.Contains("out of reach", under.Summary, StringComparison.Ordinal);

        var nothing = (await MaxBidCalculator.FindAsync(new Linear(), new Money(5_000m, "AUD"), "AU", new Money(3_000_000m, "JPY")))!;
        Assert.Null(nothing.MaxBid);
        Assert.Equal(BidRoom.BelowOpening, nothing.Room);
    }

    [Fact]
    public async Task A_budget_in_another_currency_is_refused()
    {
        Assert.Null(await MaxBidCalculator.FindAsync(new Linear(), new Money(45_000m, "NZD"), "AU", null));
    }
}

public class LotTrackerTests
{
    private static readonly DateTimeOffset Ends = new(2026, 10, 14, 15, 0, 0, TimeSpan.Zero); // midnight JST: auction Wed 14 Oct
    private static readonly Listing Lot = Fixtures.Gtr(b => b.Price = new Money(3_500_000m, "JPY")) with { AuctionEndsAt = Ends };

    private static LotSightings Seen(bool chassis = true, params LaterAppearance[] later) =>
        new(Ends.AddDays(-4), Ends.AddDays(-1), chassis, later);

    private static PricePoint P(decimal yen, int daysBefore) => new(Ends.AddDays(-daysBefore), new Money(yen, "JPY"), PriceKind.OpeningBid);

    [Fact]
    public void Before_the_auction_it_reports_opening_bid_changes()
    {
        var t = LotTracker.Track(Lot, Seen(), [P(3_800_000m, 4), P(3_500_000m, 2)], Ends.AddDays(-1));
        Assert.Equal(LotStatus.Live, t.Status);
        Assert.Equal(1, t.PriceChanges);
        Assert.Equal(-8, t.PriceChangePercent);
        Assert.Contains("¥3,800,000 to ¥3,500,000", t.Headline, StringComparison.Ordinal);
        Assert.Contains("Live bidding isn't published", t.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_car_back_at_a_later_auction_didnt_sell()
    {
        var back = new LaterAppearance(Guid.NewGuid(), new DateOnly(2026, 10, 21), "USS Tokyo", "123", new Money(3_200_000m, "JPY"));
        var t = LotTracker.Track(Lot, Seen(true, back), [P(3_500_000m, 4)], Ends.AddDays(8));
        Assert.Equal(LotStatus.Relisted, t.Status);
        Assert.Equal("Didn't sell on Wed 14 Oct: back at USS Tokyo on Wed 21 Oct.", t.Headline);
        Assert.Contains("9% lower", t.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void After_the_auction_it_waits_then_calls_it_probably_sold_but_only_with_a_chassis_number()
    {
        Assert.Equal(LotStatus.Waiting, LotTracker.Track(Lot, Seen(), [], Ends.AddDays(3)).Status);
        Assert.Equal(LotStatus.ProbablySold, LotTracker.Track(Lot, Seen(), [], Ends.AddDays(20)).Status);
        Assert.Equal(LotStatus.CantFollow, LotTracker.Track(Lot, Seen(chassis: false), [], Ends.AddDays(20)).Status);
    }
}
