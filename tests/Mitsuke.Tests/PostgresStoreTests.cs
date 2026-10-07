using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Mitsuke.Core;
using Mitsuke.Data;

namespace Mitsuke.Tests;

[Collection(PostgresTests.Name)]
public sealed class PostgresStoreTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly PostgresListingStore _listings = new(pg.Db);
    private readonly PostgresWatchlistStore _watchlists = new(pg.Db);
    private readonly PostgresAlertLog _alerts = new(pg.Db);

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static Listing Lot(string id, decimal? yen = 4_500_000m, string? frame = "BNR32-000001", DateTimeOffset? at = null) =>
        Fixtures.Gtr(b => b.Price = yen is { } y ? new Money(y, "JPY") : null) with
        {
            Key = new ListingKey("thecarapi-japan", id),
            FrameNumber = frame,
            ObservedAt = at ?? new DateTimeOffset(2026, 10, 7, 1, 0, 0, TimeSpan.Zero),
            PhotoUrls = [new Uri("https://api.thecarapi.com/auction-photo/japan/1/0"), new Uri("https://api.thecarapi.com/auction-photo/japan/1/1")],
        };

    [Fact]
    public async Task Migrations_are_idempotent()
    {
        Assert.Equal(0, await new Migrator(pg.Db, NullLogger<Migrator>.Instance).MigrateAsync());
    }

    [Fact]
    public async Task First_upsert_inserts_and_records_the_price()
    {
        var result = await _listings.UpsertAsync(Lot(Fixtures.BigId));

        Assert.True(result.IsNew);
        Assert.True(result.PriceChanged);

        await using var conn = await pg.Db.OpenConnectionAsync();
        var stored = await conn.QuerySingleAsync<(string SourceId, string[] Photos, string Grade)>(
            "select source_id, photo_urls, grade_raw from listings where id = @id", new { id = result.ListingId });
        Assert.Equal(Fixtures.BigId, stored.SourceId); // > 2^53 survives the round trip
        Assert.Equal(2, stored.Photos.Length);
        Assert.Equal("4", stored.Grade);
    }

    [Fact]
    public async Task Rescanning_an_unchanged_listing_changes_nothing()
    {
        var first = await _listings.UpsertAsync(Lot("1"));
        var second = await _listings.UpsertAsync(Lot("1", at: new DateTimeOffset(2026, 10, 7, 2, 0, 0, TimeSpan.Zero)));

        Assert.Equal(first.ListingId, second.ListingId);
        Assert.False(second.IsNew);
        Assert.False(second.PriceChanged);
        Assert.Equal(1, await CountAsync("price_observations"));
    }

    [Fact]
    public async Task A_price_change_is_logged_as_a_new_observation()
    {
        await _listings.UpsertAsync(Lot("1", yen: 4_500_000m));
        var dropped = await _listings.UpsertAsync(Lot("1", yen: 3_980_000m, at: new DateTimeOffset(2026, 10, 8, 1, 0, 0, TimeSpan.Zero)));

        Assert.True(dropped.PriceChanged);
        await using var conn = await pg.Db.OpenConnectionAsync();
        var amounts = await conn.QueryAsync<decimal>("select amount from price_observations order by observed_at");
        Assert.Equal([4_500_000m, 3_980_000m], amounts);
    }

    [Fact]
    public async Task A_relisted_car_links_to_the_same_vehicle_by_frame_number()
    {
        await _listings.UpsertAsync(Lot("week-1", frame: "BNR32-305737"));
        await _listings.UpsertAsync(Lot("week-2", frame: "BNR32-305737"));
        await _listings.UpsertAsync(Lot("other-car", frame: null));

        Assert.Equal(1, await CountAsync("vehicles"));
        await using var conn = await pg.Db.OpenConnectionAsync();
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>("select count(distinct vehicle_id) from listings where vehicle_id is not null"));
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>("select count(*) from listings where vehicle_id is null"));
    }

    [Fact]
    public async Task Listings_without_a_price_store_no_observation()
    {
        var result = await _listings.UpsertAsync(Lot("1", yen: null));
        Assert.False(result.PriceChanged);
        Assert.Equal(0, await CountAsync("price_observations"));
    }

    [Fact]
    public async Task Watchlists_round_trip()
    {
        var watchlist = new Watchlist
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
            IncludeModified = false,
            MaxPrice = new Money(4_500_000m, "JPY"),
        };
        await _watchlists.AddAsync(watchlist);

        var loaded = Assert.Single(await _watchlists.GetActiveAsync());

        Assert.Equal(watchlist with { ModelCodes = loaded.ModelCodes }, loaded);
        Assert.Equal(["BNR32"], loaded.ModelCodes);
    }

    [Fact]
    public async Task An_alert_is_claimed_once_and_retried_only_after_failure()
    {
        var (watchlistId, listingId) = await SeedAsync();

        var first = await _alerts.TryClaimAsync(watchlistId, listingId, "discord");
        Assert.NotNull(first);
        Assert.Null(await _alerts.TryClaimAsync(watchlistId, listingId, "discord")); // in flight

        await _alerts.MarkFailedAsync(first.Value, "webhook 500");
        var retry = await _alerts.TryClaimAsync(watchlistId, listingId, "discord");
        Assert.Equal(first, retry);

        await _alerts.MarkSentAsync(retry!.Value);
        Assert.Null(await _alerts.TryClaimAsync(watchlistId, listingId, "discord")); // never twice

        Assert.NotNull(await _alerts.TryClaimAsync(watchlistId, listingId, "email")); // other channels are independent
        await using var conn = await pg.Db.OpenConnectionAsync();
        Assert.Equal(2, await conn.ExecuteScalarAsync<int>("select attempts from alerts where id = @id", new { id = first.Value }));
    }

    [Fact]
    public async Task Listing_details_round_trip_through_jsonb()
    {
        var listing = await _listings.UpsertAsync(Lot("1"));
        var store = new PostgresListingDetailsStore(pg.Db);
        var details = new ListingDetails
        {
            Key = new ListingKey("thecarapi-japan", "1"),
            FetchedAt = new DateTimeOffset(2026, 10, 7, 1, 2, 3, TimeSpan.Zero),
            Sheets = [new AuctionSheet(new Uri("https://api.thecarapi.com/report-vault/japan/1/a.jpg"), new DateOnly(2026, 9, 4), IsCurrent: true)],
            Relists = [new Relist(new DateOnly(2026, 9, 4), "USS Tokyo", "123", AuctionGrade.Parse("3.5"), 48_000, new Money(3_980_000m, "JPY"), "probable", ["mileage_km"])],
            InteriorGrade = "C",
            EngineCc = 2600,
            PhotoUrls = [new Uri("https://api.thecarapi.com/auction-photo/japan/1/0")],
        };

        Assert.Null(await store.GetAsync(listing.ListingId));
        await store.SaveAsync(listing.ListingId, details);
        await store.SaveAsync(listing.ListingId, details with { InteriorGrade = "B" }); // upsert, not duplicate

        var loaded = (await store.GetAsync(listing.ListingId))!;
        Assert.Equal("B", loaded.InteriorGrade);
        Assert.Equal(details.FetchedAt, loaded.FetchedAt);
        Assert.Equal(details.Sheets, loaded.Sheets);
        Assert.Equal(details.PhotoUrls, loaded.PhotoUrls);
        var relist = Assert.Single(loaded.Relists);
        Assert.Equal((details.Relists[0].AuctionDate, details.Relists[0].OpeningBid, details.Relists[0].Grade), (relist.AuctionDate, relist.OpeningBid, relist.Grade));
        Assert.Equal(["mileage_km"], relist.Changes);
    }

    private async Task<(Guid WatchlistId, Guid ListingId)> SeedAsync()
    {
        var watchlist = new Watchlist { Id = Guid.NewGuid(), Name = "any", Make = "Nissan", Model = "Skyline" };
        await _watchlists.AddAsync(watchlist);
        var listing = await _listings.UpsertAsync(Lot("1"));
        return (watchlist.Id, listing.ListingId);
    }

    private async Task<int> CountAsync(string table)
    {
        await using var conn = await pg.Db.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<int>($"select count(*) from {table}");
    }
}
