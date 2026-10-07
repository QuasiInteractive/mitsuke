using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mitsuke.Core;
using Mitsuke.Data;
using Npgsql;

namespace Mitsuke.Tests;

/// <summary>Mitsuke.Api over HTTP against a real Postgres: the contract the web app relies on.</summary>
[Collection(PostgresTests.Name)]
public sealed class ApiTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CapturingNotifier _notifier = new();
    private WebApplicationFactory<Mitsuke.Api.LotViewBuilder> _factory = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        await pg.ResetAsync();
        _factory = new WebApplicationFactory<Mitsuke.Api.LotViewBuilder>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Testing");
            web.UseSetting("MITSUKE_DB", "Host=unused"); // replaced below by the test container
            web.ConfigureServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>();
                services.AddSingleton(pg.Db);
                services.RemoveAll<INotifier>();
                services.AddSingleton<INotifier>(_notifier);
            });
        });
        _http = _factory.CreateClient();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<Guid> SeedLotAsync()
    {
        var lot = Fixtures.Gtr() with
        {
            Key = new ListingKey("thecarapi-japan", "123"),
            AuctionHouse = "USS Tokyo",
            LotNumber = "65016",
            AuctionEndsAt = new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero),
            Attribution = "USS Tokyo auction via TheCarApi",
        };
        return (await new PostgresListingStore(pg.Db).UpsertAsync(lot)).ListingId;
    }

    [Fact]
    public async Task Lot_view_has_everything_the_page_needs()
    {
        var id = await SeedLotAsync();

        var json = await _http.GetFromJsonAsync<JsonElement>($"/api/lots/{id}");

        Assert.Equal("1991 Nissan Skyline (BNR32)", json.GetProperty("title").GetString());
        Assert.Equal("JPY", json.GetProperty("openingBid").GetProperty("currency").GetString());
        Assert.Equal("AUD", json.GetProperty("landed").GetProperty("total").GetProperty("currency").GetString());
        Assert.True(json.GetProperty("landed").GetProperty("lines").GetArrayLength() > 3);
        Assert.Equal("2026-10-09", json.GetProperty("auctionDay").GetString());
        Assert.Equal("USS Tokyo auction via TheCarApi", json.GetProperty("attribution").GetString());
        Assert.Contains("Estimates only", json.GetProperty("disclaimer").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, json.GetProperty("priceHistory").GetArrayLength());
    }

    [Fact]
    public async Task Landed_cost_follows_the_requested_destination()
    {
        var id = await SeedLotAsync();
        var json = await _http.GetFromJsonAsync<JsonElement>($"/api/lots/{id}?to=nz");
        Assert.Equal("NZD", json.GetProperty("landed").GetProperty("total").GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Unknown_lot_is_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/lots/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Bid_request_is_stored_and_passed_on()
    {
        var id = await SeedLotAsync();

        var res = await _http.PostAsJsonAsync($"/api/lots/{id}/bid-requests",
            new { maxBidJpy = 2_500_000, name = "Test Buyer", email = "buyer@example.com", note = "Prefer Brisbane" });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        await using var conn = await pg.Db.OpenConnectionAsync();
        Assert.Equal(2_500_000m, await conn.ExecuteScalarAsync<decimal>("select max_bid_jpy from bid_requests"));
        var message = Assert.Single(_notifier.Sent);
        Assert.Contains("Max bid: ¥2,500,000", message, StringComparison.Ordinal);
        Assert.Contains("USS Tokyo lot 65016", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bad_bid_requests_get_field_errors_and_store_nothing()
    {
        var id = await SeedLotAsync();

        var res = await _http.PostAsJsonAsync($"/api/lots/{id}/bid-requests", new { maxBidJpy = 5, name = "", email = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("maxBidJpy", out _));
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public async Task Bid_requests_are_rate_limited_per_client()
    {
        var id = await SeedLotAsync();
        var body = new { maxBidJpy = 2_500_000, name = "A", email = "a@example.com" };
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/lots/{id}/bid-requests") { Content = JsonContent.Create(body) };
            req.Headers.Add("X-Forwarded-For", "203.0.113.7");
            statuses.Add((await _http.SendAsync(req)).StatusCode);
        }
        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task Matches_list_alerted_lots_as_cards()
    {
        var id = await SeedLotAsync();
        var watchlist = new Watchlist { Id = Guid.NewGuid(), Name = "R32 GT-R", Make = "Nissan", Model = "Skyline" };
        await new PostgresWatchlistStore(pg.Db).AddAsync(watchlist);
        var alerts = new PostgresAlertLog(pg.Db);
        await alerts.MarkSentAsync((await alerts.TryClaimAsync(watchlist.Id, id, "console"))!.Value);

        var cards = await _http.GetFromJsonAsync<JsonElement>("/api/matches");

        var card = Assert.Single(cards.EnumerateArray());
        Assert.Equal(id, card.GetProperty("id").GetGuid());
        Assert.Equal("R32 GT-R", card.GetProperty("watchlistName").GetString());
        Assert.Equal("AUD", card.GetProperty("landedTotal").GetProperty("currency").GetString());
    }

    private sealed class CapturingNotifier : INotifier
    {
        public List<string> Sent { get; } = [];
        public string Channel => "test";

        public Task SendAsync(string message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
