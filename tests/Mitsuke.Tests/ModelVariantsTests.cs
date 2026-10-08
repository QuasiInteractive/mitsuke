using Kensaya.Worker.Core.Rules;
using Microsoft.Extensions.Time.Testing;
using Mitsuke.Core;
using Mitsuke.Data;
using Mitsuke.Pricing;

namespace Mitsuke.Tests;

public class ModelVariantsTests
{
    private static Listing Evo(string code, int year, int? month) =>
        Fixtures.Gtr(b => { b.Make = "Mitsubishi"; b.Model = "Lancer"; b.ModelCode = code; }) with { Year = year, Month = month };

    private static ListingDetails Gears(int? gears) => new() { Key = new ListingKey("t", "1"), FetchedAt = DateTimeOffset.UnixEpoch, ManualGears = gears };

    [Fact]
    public void A_build_date_inside_one_generation_is_certain()
    {
        var v = ModelVariants.Identify(Evo("CT9A", 2006, 3))!;
        Assert.Equal(("Lancer Evolution IX", true), (v.Name, v.Certain));
        Assert.Equal("CT9A built Mar 2006: the Evo IX years.", v.Reason);
    }

    [Fact]
    public void A_changeover_month_is_settled_by_a_6_speed_on_the_sheet()
    {
        // The real lot from 8 Oct 2026: CT9A built Jan 2003 with "F6" on the sheet.
        var v = ModelVariants.Identify(Evo("CT9A", 2003, 1), Gears(6))!;
        Assert.Equal(("Lancer Evolution VIII", false), (v.Name, v.Certain)); // the sheet read isn't cross-checked
        Assert.Contains("rules out the Evo VII (5-speed only)", v.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_tie_breaker_it_says_both()
    {
        var v = ModelVariants.Identify(Evo("CT9A", 2003, 1), Gears(5))!;
        Assert.Equal("Lancer Evolution VII or Evo VIII", v.Name);
        Assert.False(v.Certain);
        Assert.Contains("build plate", v.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Year_only_still_narrows_it_down()
    {
        var vi = ModelVariants.Identify(Evo("CP9A", 2000, null))!;
        Assert.Equal(("Lancer Evolution VI", false), (vi.Name, vi.Certain)); // no month, so not certain
        Assert.Equal("Lancer Evolution VII or Evo VIII", ModelVariants.Identify(Evo("CT9A", 2003, null))!.Name);
    }

    [Fact]
    public void Unknown_cars_get_no_guess()
    {
        Assert.Null(ModelVariants.Identify(Fixtures.Gtr()));                 // codes that span one generation need no table
        Assert.Null(ModelVariants.Identify(Evo("CT9A", 1995, 1)));           // outside every window: don't guess
    }

    [Fact]
    public async Task Eligibility_uses_the_build_month()
    {
        var now = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        var checker = new KensayaEligibilityChecker(new FileRulesSource(Landed.Catalog), new FakeTimeProvider(now));

        // Built Sep 2001: 25 in Sep 2026. Without the month the engine assumes December and says not yet.
        Assert.Equal(EligibilityVerdict.Yes, (await checker.CheckAsync(Evo("CT9A", 2001, 9), "AU"))!.Verdict);
        Assert.NotEqual(EligibilityVerdict.Yes, (await checker.CheckAsync(Evo("CT9A", 2001, null), "AU"))!.Verdict);
    }
}

[Collection(PostgresTests.Name)]
public sealed class ListingMonthStoreTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_build_month_survives_a_round_trip()
    {
        var store = new PostgresListingStore(pg.Db);
        var id = (await store.UpsertAsync(Fixtures.Gtr() with { Month = 8 })).ListingId;
        Assert.Equal(8, (await store.GetAsync(id))!.Month);
    }
}
