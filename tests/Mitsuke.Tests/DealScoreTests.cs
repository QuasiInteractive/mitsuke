using Mitsuke.Core;
using Mitsuke.Data;
using Mitsuke.Sources.TheCarApi;

namespace Mitsuke.Tests;

public class OpeningRangeTests
{
    [Fact]
    public void The_range_is_the_10th_to_90th_percentile_of_real_opening_bids()
    {
        decimal[] bids = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        Assert.Equal(1m, DealScorer.Percentile(bids, 0.1m));
        Assert.Equal(9m, DealScorer.Percentile(bids, 0.9m));
        Assert.Equal(4_000_000m, DealScorer.Percentile([4_000_000m], 0.9m)); // one value is its own range
    }
}

public class DealScorerTests
{
    private static Listing Subject(decimal yen = 3_000_000m, int km = 87_000, string grade = "4", int year = 1991) =>
        Fixtures.Gtr(b => { b.Price = new Money(yen, "JPY"); b.MileageKm = km; b.Grade = grade; b.Year = year; });

    private static int _n;

    private static Comparable Comp(decimal yen, int km = 90_000, decimal? grade = 4m, int year = 1991, bool repaired = false, string currency = "JPY") =>
        new(new ListingKey("t", (++_n).ToString(System.Globalization.CultureInfo.InvariantCulture)), year, km, grade, repaired, new Money(yen, currency));

    private static List<Comparable> Spread(int count, decimal from = 2_000_000m, decimal step = 200_000m) =>
        Enumerable.Range(0, count).Select(i => Comp(from + i * step)).ToList();

    [Fact]
    public void Cheapest_of_its_peers_scores_near_100_and_says_how_far_under_typical()
    {
        var deal = DealScorer.Score(Subject(yen: 1_500_000m), Spread(20));

        Assert.Equal(100, deal.Score);
        Assert.Equal("Great value", deal.Label);
        Assert.Equal(ScoreConfidence.Normal, deal.Confidence);
        Assert.Equal(20, deal.ComparableCount);
        Assert.Equal(new Money(3_900_000m, "JPY"), deal.Typical); // median of 2.0M..5.8M in 200k steps
        Assert.Equal(new Money(2_400_000m, "JPY"), deal.BelowTypical);
    }

    [Fact]
    public void Dearest_scores_0_and_typical_scores_about_50()
    {
        Assert.Equal(0, DealScorer.Score(Subject(yen: 9_000_000m), Spread(20)).Score);

        var middle = DealScorer.Score(Subject(yen: 4_000_000m), Spread(21)); // exactly the median of 2.0M..6.0M
        Assert.Equal(50, middle.Score);
        Assert.Equal("Typical price", middle.Label);
    }

    [Fact]
    public void Too_few_comparables_gives_no_score_rather_than_a_misleading_one()
    {
        var deal = DealScorer.Score(Subject(), Spread(4));
        Assert.Null(deal.Score);
        Assert.Equal(ScoreConfidence.None, deal.Confidence);
        Assert.Equal("Not enough similar cars yet (4)", deal.Label);
    }

    [Fact]
    public void Prefers_similar_mileage_and_grade_and_falls_back_to_broad_with_low_confidence()
    {
        // 10 close matches (cheap) + 10 high-mileage repaired cars (dearer, should be ignored when tight works).
        var close = Enumerable.Range(0, 10).Select(i => Comp(2_000_000m + i * 100_000m)).ToList();
        var far = Enumerable.Range(0, 10).Select(i => Comp(9_000_000m, km: 250_000, grade: null, repaired: true)).ToList();

        var tight = DealScorer.Score(Subject(yen: 2_450_000m), [.. close, .. far]);
        Assert.Equal(10, tight.ComparableCount);
        Assert.Contains("similar mileage and grade", tight.Basis, StringComparison.Ordinal);
        Assert.Equal(ScoreConfidence.Low, tight.Confidence); // tight but under 15

        var broad = DealScorer.Score(Subject(yen: 2_450_000m), [.. close.Take(5), .. far]);
        Assert.Equal(15, broad.ComparableCount);
        Assert.Contains("any mileage and grade", broad.Basis, StringComparison.Ordinal);
        Assert.Equal(ScoreConfidence.Low, broad.Confidence);
    }

    [Fact]
    public void Ignores_other_currencies_and_unpriced_subjects()
    {
        var aud = Enumerable.Range(0, 20).Select(_ => Comp(1m, currency: "AUD")).ToList();
        Assert.Null(DealScorer.Score(Subject(), aud).Score);
        Assert.Null(DealScorer.Score(Fixtures.Gtr(b => b.Price = null), Spread(20)).Score);
    }

    [Theory]
    [InlineData(1991, 87_000, "4", true)]
    [InlineData(1993, 87_000, "4", false)]   // two years apart
    [InlineData(1991, 150_000, "4", false)]  // mileage well outside ±30%
    [InlineData(1991, 87_000, "3", false)]   // grade more than half a point off
    [InlineData(1991, 87_000, "R", false)]   // repaired vs not
    public void Tight_matching_rules(int year, int km, string grade, bool expected)
    {
        var parsed = AuctionGrade.Parse(grade)!;
        var comp = new Comparable(new ListingKey("t", "x"), year, km, parsed.Score, parsed.IsRepaired, new Money(1m, "JPY"));
        Assert.Equal(expected, DealScorer.IsTightMatch(Subject(), comp));
    }

    [Fact]
    public void Alert_shows_score_label_basis_and_gap_to_typical()
    {
        var watchlist = new Watchlist { Id = Guid.NewGuid(), Name = "R32", Make = "Nissan", Model = "Skyline" };
        var text = AlertFormatter.Format(watchlist, Subject(yen: 1_500_000m), deal: DealScorer.Score(Subject(yen: 1_500_000m), Spread(20)));
        Assert.Contains("Deal score 100/100 · Great value (vs 20 similar BNR32s, 1991, similar mileage and grade); ¥2,400,000 under the typical opening bid.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Archive_search_uses_the_archive_endpoint_with_the_same_filters()
    {
        Assert.Equal(
            "/api/archive/search?site=japan&brand=Nissan&model=Skyline&production_year_from=1989&production_year_to=1994&limit=100&offset=0",
            TheCarApiSource.BuildSearchUrl(new SourceQuery("Nissan", "Skyline", 1989, 1994), 0, 100, "/api/archive/search"));
    }
}

[Collection(PostgresTests.Name)]
public sealed class ComparablesStoreTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly PostgresListingStore _listings = new(pg.Db);
    private readonly PostgresComparablesStore _comparables = new(pg.Db);

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static Listing Lot(string id, decimal yen, string? frame = null, string modelCode = "BNR32", int year = 1991, int day = 1) =>
        Fixtures.Gtr(b => { b.Price = new Money(yen, "JPY"); b.ModelCode = modelCode; b.Year = year; }) with
        {
            Key = new ListingKey("thecarapi-japan", id),
            FrameNumber = frame,
            ObservedAt = new DateTimeOffset(2026, 10, day, 0, 0, 0, TimeSpan.Zero),
        };

    [Fact]
    public async Task One_row_per_physical_car_at_its_latest_price_and_never_the_subject()
    {
        var subject = Lot("subject", 3_000_000m, frame: "BNR32-000001");
        await _listings.UpsertAsync(subject);
        await _listings.UpsertAsync(Lot("subject-last-week", 3_100_000m, frame: "BNR32-000001")); // same car, earlier listing

        await _listings.UpsertAsync(Lot("relist-1", 4_000_000m, frame: "BNR32-000002", day: 1));
        await _listings.UpsertAsync(Lot("relist-2", 3_800_000m, frame: "BNR32-000002", day: 8)); // newest listing of that car
        await _listings.UpsertAsync(Lot("no-frame", 2_500_000m));
        await _listings.UpsertAsync(Lot("price-dropped", 5_000_000m, day: 1));
        await _listings.UpsertAsync(Lot("price-dropped", 4_500_000m, day: 3));

        await _listings.UpsertAsync(Lot("gts", 1_000_000m, modelCode: "HCR32"));   // different model code
        await _listings.UpsertAsync(Lot("r34", 9_000_000m, year: 1999));          // outside ±3 years

        var comps = await _comparables.GetCandidatesAsync(subject);

        Assert.Equal(
            ["no-frame:2500000", "price-dropped:4500000", "relist-2:3800000"],
            comps.Select(c => $"{c.Key.SourceId}:{c.Price.Amount:0}").Order(StringComparer.Ordinal));
    }
}
