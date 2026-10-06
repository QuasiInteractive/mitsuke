using Mitsuke.Core;

namespace Mitsuke.Tests;

public class WatchlistMatcherTests
{
    private static readonly Watchlist R32Gtr = new()
    {
        Id = Guid.NewGuid(),
        Name = "R32 GT-R",
        Make = "Nissan",
        Model = "Skyline",
        ModelCodes = new HashSet<string> { "BNR32" },
        YearFrom = 1989,
        YearTo = 1994,
        MaxMileageKm = 150_000,
        MinGrade = 3.5m,
        MaxPrice = new Money(6_000_000m, "JPY"),
    };

    [Fact]
    public void Matching_car_has_no_mismatches()
    {
        Assert.Empty(WatchlistMatcher.Mismatches(R32Gtr, Fixtures.Gtr()));
        Assert.True(WatchlistMatcher.IsMatch(R32Gtr, Fixtures.Gtr()));
    }

    [Fact]
    public void Make_and_model_compare_case_insensitively()
    {
        Assert.True(WatchlistMatcher.IsMatch(R32Gtr, Fixtures.Gtr(b => { b.Make = "NISSAN"; b.Model = "skyline"; })));
    }

    [Fact]
    public void Non_gtr_skyline_is_rejected_by_model_code()
    {
        var gtst = Fixtures.Gtr(b => b.ModelCode = "HCR32");
        Assert.Contains("model code HCR32", WatchlistMatcher.Mismatches(R32Gtr, gtst));
    }

    [Fact]
    public void Repaired_cars_are_excluded_unless_asked_for()
    {
        var repaired = Fixtures.Gtr(b => b.Grade = "R");
        Assert.False(WatchlistMatcher.IsMatch(R32Gtr, repaired));
        Assert.True(WatchlistMatcher.IsMatch(R32Gtr with { IncludeRepaired = true, MinGrade = null }, repaired));
    }

    [Fact]
    public void Grade_below_minimum_is_rejected()
    {
        Assert.False(WatchlistMatcher.IsMatch(R32Gtr, Fixtures.Gtr(b => b.Grade = "3")));
    }

    [Fact]
    public void Unknown_values_fail_filters_that_need_them()
    {
        // Never alert on a guess: no mileage, no grade or no price means "not proven to match".
        Assert.False(WatchlistMatcher.IsMatch(R32Gtr, Fixtures.Gtr(b => b.MileageKm = null)));
        Assert.False(WatchlistMatcher.IsMatch(R32Gtr, Fixtures.Gtr(b => b.Grade = "***")));
        Assert.False(WatchlistMatcher.IsMatch(R32Gtr, Fixtures.Gtr(b => b.Price = null)));
        Assert.False(WatchlistMatcher.IsMatch(R32Gtr, Fixtures.Gtr(b => b.Year = null)));
    }

    [Fact]
    public void Unknown_values_pass_when_no_filter_needs_them()
    {
        var loose = new Watchlist { Id = Guid.NewGuid(), Name = "any Skyline", Make = "Nissan", Model = "Skyline" };
        Assert.True(WatchlistMatcher.IsMatch(loose, Fixtures.Gtr(b => { b.MileageKm = null; b.Grade = null; b.Price = null; b.ModelCode = null; })));
    }

    [Fact]
    public void Prices_in_another_currency_never_match()
    {
        var aud = Fixtures.Gtr(b => b.Price = new Money(1m, "AUD"));
        Assert.Contains("price in AUD, limit in JPY", WatchlistMatcher.Mismatches(R32Gtr, aud));
    }

    [Fact]
    public void Over_budget_is_rejected()
    {
        Assert.False(WatchlistMatcher.IsMatch(R32Gtr, Fixtures.Gtr(b => b.Price = new Money(6_000_001m, "JPY"))));
    }

    [Fact]
    public void Modified_cars_can_be_excluded()
    {
        var modified = Fixtures.Gtr(b => b.IsModified = true);
        Assert.True(WatchlistMatcher.IsMatch(R32Gtr, modified));
        Assert.False(WatchlistMatcher.IsMatch(R32Gtr with { IncludeModified = false }, modified));
    }

    [Fact]
    public void Auctions_already_underway_are_rejected_when_a_clock_is_given()
    {
        // 9pm in Japan on Tue 6 Oct; that day's auctions end at midnight JST.
        var now = new DateTimeOffset(2026, 10, 6, 21, 0, 0, TimeSpan.FromHours(9));
        var today = Fixtures.Gtr() with { AuctionEndsAt = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.FromHours(9)) };
        var tomorrow = Fixtures.Gtr() with { AuctionEndsAt = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.FromHours(9)) };

        Assert.Contains("too late to bid", WatchlistMatcher.Mismatches(R32Gtr, today, now));
        Assert.True(WatchlistMatcher.IsMatch(R32Gtr, tomorrow, now));
        Assert.True(WatchlistMatcher.IsMatch(R32Gtr, today)); // no clock, no time filter
    }
}
