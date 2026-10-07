using Kensaya.Worker.Core.Rules;
using Mitsuke.Core;
using Mitsuke.Pricing;

namespace Mitsuke.Tests;

/// <summary>The real engine with the rules files' own exchange rates: deterministic, no network.</summary>
internal static class Landed
{
    public static readonly RulesCatalog Catalog = RulesCatalog.Load();
    public static readonly KensayaLandedCostEstimator Estimator = new(new FileRulesSource(Catalog));
}

public class LandedEstimateTests
{
    private static readonly Money Bid = new(3_980_000m, "JPY");

    [Fact]
    public async Task Estimates_in_the_destination_currency_with_a_range_and_breakdown()
    {
        var e = (await Landed.Estimator.EstimateAsync(Bid, "AU"))!;

        Assert.Equal("AU", e.Destination);
        Assert.Equal("AUD", e.Total.Currency);
        Assert.True(e.Low.Amount <= e.Total.Amount && e.Total.Amount <= e.High.Amount);
        Assert.Contains(e.Lines, l => l.Id == "vehicle");
        Assert.False(e.FxLive); // file rate in tests

        // Same numbers as Kensa-ya's engine called directly.
        var direct = LandedCost.Compute(Landed.Catalog.GetRules("AU")!, new LandedCostInput(3_980_000));
        Assert.Equal((decimal)direct.Total, e.Total.Amount);
    }

    [Fact]
    public async Task Costs_more_for_a_dearer_car()
    {
        var cheap = await Landed.Estimator.EstimateAsync(new Money(2_000_000m, "JPY"), "AU");
        var dear = await Landed.Estimator.EstimateAsync(new Money(8_000_000m, "JPY"), "AU");
        Assert.True(dear!.Total.Amount > cheap!.Total.Amount);
    }

    [Fact]
    public async Task Works_for_new_zealand_in_nzd()
    {
        Assert.Equal("NZD", (await Landed.Estimator.EstimateAsync(Bid, "nz"))!.Total.Currency);
    }

    [Fact]
    public async Task Non_yen_prices_and_unknown_destinations_have_no_estimate()
    {
        Assert.Null(await Landed.Estimator.EstimateAsync(new Money(1m, "AUD"), "AU"));
        Assert.Null(await Landed.Estimator.EstimateAsync(Bid, "ZZ"));
    }

    private static LandedEstimate Estimate(decimal total, string currency = "AUD") => new()
    {
        Destination = "AU",
        Total = new Money(total, currency),
        Low = new Money(total - 5_000, currency),
        High = new Money(total + 5_000, currency),
        JpyPerUnit = 110,
    };

    private static readonly Watchlist UnderA45k = new()
    {
        Id = Guid.NewGuid(),
        Name = "R32 GT-R",
        Make = "Nissan",
        Model = "Skyline",
        MaxLanded = new Money(45_000m, "AUD"),
    };

    [Fact]
    public void Landed_budget_matches_on_the_estimate_midpoint()
    {
        Assert.True(WatchlistMatcher.IsMatch(UnderA45k, Fixtures.Gtr(), landed: Estimate(44_999)));
        Assert.Contains("landed 46,000 AUD > 45,000 AUD", WatchlistMatcher.Mismatches(UnderA45k, Fixtures.Gtr(), landed: Estimate(46_000)));
    }

    [Fact]
    public void Landed_budget_never_matches_without_an_estimate_or_in_another_currency()
    {
        Assert.Contains("no landed estimate", WatchlistMatcher.Mismatches(UnderA45k, Fixtures.Gtr()));
        Assert.Contains("landed in NZD, budget in AUD", WatchlistMatcher.Mismatches(UnderA45k, Fixtures.Gtr(), landed: Estimate(1, "NZD")));
    }

    [Fact]
    public void Alert_leads_with_the_landed_estimate_and_range()
    {
        var text = AlertFormatter.Format(UnderA45k, Fixtures.Gtr(), landed: Estimate(42_350.40m));
        Assert.Contains("Est. landed in AU: A$42,350 (range A$37,350–47,350)", text, StringComparison.Ordinal);
        Assert.Contains("Opening bid: ¥4,500,000", text, StringComparison.Ordinal);
    }
}
