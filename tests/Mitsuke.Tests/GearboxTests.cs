using Mitsuke.Core;

namespace Mitsuke.Tests;

public class GearboxTests
{
    [Theory]
    [InlineData("Manual", "manual")]
    [InlineData("F5", "manual")]
    [InlineData("Automatic", "automatic")]
    [InlineData("CVT", "automatic")]
    [InlineData("AT", "automatic")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("Other", null)]
    public void Classifies_what_the_feed_says(string? transmission, string? expected) =>
        Assert.Equal(expected, Gearbox.Class(transmission));

    [Fact]
    public void Only_two_known_different_gearboxes_differ() =>
        Assert.Equal([true, false, false, false],
            new[] { Gearbox.Differ("Manual", "Automatic"), Gearbox.Differ("Manual", "Manual"), Gearbox.Differ("Manual", null), Gearbox.Differ(null, null) });

    private static int _n;

    private static Comparable Comp(decimal yen, string? transmission) =>
        new(new ListingKey("t", (++_n).ToString(System.Globalization.CultureInfo.InvariantCulture)), 1991, 90_000, 4m, false, new Money(yen, "JPY"), transmission);

    [Fact]
    public void An_automatic_is_scored_against_automatics_not_dearer_manuals()
    {
        var auto = Fixtures.Gtr(b => { b.Price = new Money(5_000_000m, "JPY"); b.MileageKm = 90_000; }) with { Transmission = "Automatic" };
        var comps = Enumerable.Range(0, 10).Select(i => Comp(7_000_000m + i * 100_000m, "Manual"))
            .Concat(Enumerable.Range(0, 8).Select(i => Comp(4_600_000m + i * 100_000m, "Automatic"))).ToList();

        var deal = DealScorer.Score(auto, comps);

        Assert.Equal(8, deal.ComparableCount);                     // the manuals are left out
        Assert.Equal(new Money(4_950_000m, "JPY"), deal.Typical);  // an automatic's usual price
        Assert.Contains("automatic", deal.Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void Too_few_of_the_same_gearbox_falls_back_to_all_of_them()
    {
        var auto = Fixtures.Gtr() with { Transmission = "Automatic" };
        var comps = Enumerable.Range(0, 10).Select(i => Comp(4_000_000m + i * 100_000m, "Manual")).ToList();
        var deal = DealScorer.Score(auto, comps);
        Assert.Equal(10, deal.ComparableCount);
        Assert.DoesNotContain("automatic", deal.Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void The_market_meter_for_an_automatic_counts_automatics_and_unknowns()
    {
        var day = new DateOnly(2026, 10, 5);
        var samples = Enumerable.Range(0, 6).Select(_ => new MarketSample(day, 7_000_000m, false, "Manual"))
            .Concat(Enumerable.Range(0, 5).Select(_ => new MarketSample(day, 5_000_000m, false, "Automatic")))
            .Append(new MarketSample(day, 5_200_000m, false)).ToList();

        var meter = MarketMeters.Build("NA1", "JPY", samples, 5_000_000m, "Automatic")!;

        Assert.Equal("NA1 automatic", meter.Name);
        Assert.Equal(6, meter.Cars);
        Assert.Equal(MarketSignal.About, meter.Signal);
        Assert.Equal(MarketSignal.Below, MarketMeters.Build("NA1", "JPY", samples, 5_000_000m)!.Signal); // mixed: looks cheaper than it is
    }
}
