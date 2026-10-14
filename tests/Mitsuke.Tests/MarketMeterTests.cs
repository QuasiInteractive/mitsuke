using Dapper;
using Mitsuke.Core;
using Mitsuke.Data;

namespace Mitsuke.Tests;

public class MarketMeterTests
{
    private static readonly DateOnly Monday = new(2026, 9, 21);

    private static List<MarketSample> Week(int week, params decimal[] amounts) =>
        amounts.Select((a, i) => new MarketSample(Monday.AddDays(7 * week + i % 5), a, OnAuctionNow: false)).ToList();

    [Fact]
    public void Too_few_cars_says_nothing_rather_than_something_misleading()
    {
        Assert.Null(MarketMeters.Build("BNR32", "JPY", Week(0, 1, 2, 3, 4)));
    }

    [Fact]
    public void Places_a_lot_against_the_typical_opening_bid()
    {
        var samples = Week(0, 2_000_000, 2_500_000, 3_000_000, 3_500_000, 4_000_000);

        var cheap = MarketMeters.Build("BNR32", "JPY", samples, 2_400_000)!;
        Assert.Equal(3_000_000, cheap.Typical);
        Assert.Equal(-20, cheap.VsTypicalPercent);
        Assert.Equal(MarketSignal.Below, cheap.Signal);
        Assert.StartsWith("Below usual: 20% under the typical opening bid for a BNR32.", cheap.Summary, StringComparison.Ordinal);

        Assert.Equal(MarketSignal.About, MarketMeters.Build("BNR32", "JPY", samples, 3_200_000)!.Signal);
        Assert.Equal(MarketSignal.Above, MarketMeters.Build("BNR32", "JPY", samples, 3_300_000)!.Signal);
        Assert.Null(MarketMeters.Build("BNR32", "JPY", samples)!.Signal); // a watchlist's meter has no lot in it
    }

    [Fact]
    public void Trend_waits_for_three_solid_weeks_then_compares_first_and_last()
    {
        var two = Week(0, 3_000_000, 3_000_000, 3_000_000).Concat(Week(1, 2_700_000, 2_700_000, 2_700_000)).ToList();
        var early = MarketMeters.Build("BNR32", "JPY", two)!;
        Assert.Null(early.TrendPercent);
        Assert.Contains("Trend shows once there are a few weeks of history.", early.Summary, StringComparison.Ordinal);

        // A thin week (two cars) is plotted but doesn't count towards the trend.
        var three = two.Concat(Week(2, 9_000_000, 9_000_000)).Concat(Week(3, 2_700_000, 2_700_000, 2_700_000)).ToList();
        var meter = MarketMeters.Build("BNR32", "JPY", three)!;
        Assert.Equal(4, meter.Weeks.Count);
        Assert.Equal(-10m, meter.TrendPercent);
        Assert.Contains("Opening bids down 10% over 3 weeks.", meter.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Weeks_start_on_monday_and_supply_counts_lots_still_at_auction()
    {
        Assert.Equal(new DateOnly(2026, 10, 5), MarketMeters.WeekOf(new DateOnly(2026, 10, 11))); // Sunday
        Assert.Equal(new DateOnly(2026, 10, 5), MarketMeters.WeekOf(new DateOnly(2026, 10, 5)));

        var samples = Week(0, 1, 2, 3, 4, 5).Select((s, i) => s with { OnAuctionNow = i < 2 }).ToList();
        Assert.Equal(2, MarketMeters.Build("X", "JPY", samples)!.OnAuctionNow);
    }

    [Fact]
    public void The_dot_strip_is_capped_but_keeps_both_ends()
    {
        var many = Week(0, Enumerable.Range(1, 500).Select(i => (decimal)i).ToArray());
        var meter = MarketMeters.Build("X", "JPY", many)!;
        Assert.Equal(80, meter.Spread.Count);
        Assert.Equal(1, meter.Spread[0]);
        Assert.Equal(500, meter.Spread[^1]);
        Assert.Equal(500, meter.Cars);
    }
}

public class MarketHistoryTests
{
    [Fact]
    public void Months_carry_typical_range_and_extremes_and_skip_thin_ones()
    {
        var oct = new DateOnly(2026, 10, 3);
        var samples = Enumerable.Range(2, 5).Select(a => new MarketSample(oct, a * 1_000_000m, false))
            .Append(new MarketSample(new DateOnly(2026, 9, 20), 9_000_000m, false))   // one car in September: not a month
            .ToList();

        var month = Assert.Single(MarketHistories.Monthly(samples));
        Assert.Equal(new DateOnly(2026, 10, 1), month.Month);
        Assert.Equal(4_000_000m, month.Typical);
        Assert.Equal(2_000_000m, month.Min);
        Assert.Equal(6_000_000m, month.Max);
        Assert.Equal(5, month.Cars);
    }
}

[Collection(PostgresTests.Name)]
public sealed class PostgresMarketStoreTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly PostgresListingStore _listings = new(pg.Db);
    private readonly PostgresMarketStore _market = new(pg.Db);
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> AddAsync(string id, decimal yen, int daysAgo = 2, string grade = "4", int km = 80_000, string? frame = null,
        string lot = "100", string code = "BNR32")
    {
        var at = Now.AddDays(-daysAgo);
        var listing = Fixtures.Gtr(b => { b.Price = new Money(yen, "JPY"); b.Grade = grade; b.MileageKm = km; b.ModelCode = code; }) with
        {
            Key = new ListingKey("thecarapi-japan", id),
            FrameNumber = frame,
            LotNumber = lot + id,
            AuctionEndsAt = at.AddDays(3),
            ObservedAt = at,
        };
        return (await _listings.UpsertAsync(listing)).ListingId;
    }

    [Fact]
    public async Task Samples_are_one_per_car_per_week_with_supply()
    {
        await AddAsync("1", 3_000_000, frame: "BNR32-1", daysAgo: 20);
        await AddAsync("2", 2_900_000, frame: "BNR32-1", daysAgo: 19); // same car, same week: once
        await AddAsync("3", 2_800_000, frame: "BNR32-1", daysAgo: 6);  // relisted a fortnight later: again
        await AddAsync("4", 4_000_000, daysAgo: 1);                     // on auction now
        await AddAsync("5", 1_000_000, code: "BCNR33");                  // another chassis code

        var samples = await _market.GetSamplesAsync(new MarketScope("Nissan", "Skyline", ["BNR32"]), Now.AddDays(-84), Now);

        Assert.Equal(3, samples.Count);
        Assert.Single(samples, s => s.OnAuctionNow);
        Assert.DoesNotContain(samples, s => s.Amount == 1_000_000);
    }

    [Fact]
    public async Task Models_with_enough_cars_are_listed_one_row_per_chassis_code()
    {
        for (var i = 0; i < 6; i++) await AddAsync($"r{i}", 3_000_000 + i * 100_000, daysAgo: 10);
        for (var i = 0; i < 2; i++) await AddAsync($"s{i}", 2_000_000, daysAgo: 10, code: "BCNR33");

        var models = await _market.ListModelsAsync(Now.AddDays(-365), Now, 5);

        var r32 = Assert.Single(models);
        Assert.Equal("BNR32", r32.Code);
        Assert.Equal("Nissan", r32.Make);
        Assert.Equal(6, r32.Cars);
        Assert.Equal(3_250_000m, r32.Typical);
    }

    [Fact]
    public async Task Scan_finds_clean_cars_well_under_typical_and_skips_placeholders_and_rough_ones()
    {
        for (var i = 0; i < 10; i++) await AddAsync($"t{i}", 3_000_000 + i * 50_000, daysAgo: 30);
        var steal = await AddAsync("steal", 2_000_000, daysAgo: 3);
        await AddAsync("placeholder", 10_000, daysAgo: 3);
        await AddAsync("rough", 2_000_000, daysAgo: 3, grade: "R");
        await AddAsync("tired", 2_000_000, daysAgo: 3, km: 200_000);
        await AddAsync("old-news", 2_000_000, daysAgo: 40);
        var archived = await AddAsync("archived", 2_000_000, daysAgo: 3);
        await using (var c = await pg.Db.OpenConnectionAsync()) // seen this week, but its auction was in July
            await c.ExecuteAsync("update listings set auction_ends_at = @at where id = @archived", new { at = Now.AddDays(-100), archived });

        // A month start in Japan time, as the home page passes it: must not trip Npgsql's UTC-only rule.
        var found = await _market.ScanAsync(Now.AddDays(-10).ToOffset(TimeSpan.FromHours(9)), Now, 10);

        var pick = Assert.Single(found);
        Assert.Equal(steal, pick.ListingId);
        Assert.Equal("BNR32", pick.Group);
        Assert.True(pick.Typical > 2_900_000);

        await using var conn = await pg.Db.OpenConnectionAsync();
        Assert.Equal(16, await conn.ExecuteScalarAsync<int>("select count(*) from listings"));
    }
}
