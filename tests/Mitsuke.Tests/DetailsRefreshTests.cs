using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Mitsuke.Core;
using Mitsuke.Data;
using Npgsql;

namespace Mitsuke.Tests;

/// <summary>Lots saved before a new detail field existed get it in the background, once, without the page fetching it.</summary>
[Collection(PostgresTests.Name)]
public sealed class DetailsRefreshTests(PostgresFixture pg) : IAsyncLifetime
{
    private static readonly ListingKey Key = new("fake", "lot-1");
    private readonly FakeDetails _source = new();
    private readonly RecordingRequests _requests = new();
    private Guid _listingId;

    public async Task InitializeAsync()
    {
        await pg.ResetAsync();
        _listingId = (await new PostgresListingStore(pg.Db).UpsertAsync(Fixtures.Gtr() with { Key = Key })).ListingId;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private PostgresListingDetailsStore Store => new(pg.Db);

    private DetailsRefresher Refresher() =>
        new(new PostgresListingStore(pg.Db), [_source], Store, NullLogger<DetailsRefresher>.Instance);

    private static ListingDetails Details(int format) => new()
    {
        Key = Key, FetchedAt = DateTimeOffset.UnixEpoch, Format = format,
        Spec = format >= ListingDetails.CurrentFormat ? [new SpecItem("Colour", "White", true)] : [],
    };

    [Fact]
    public async Task Old_format_details_are_fetched_again_and_saved()
    {
        await Store.SaveAsync(_listingId, Details(format: 0));

        Assert.True(await Refresher().RefreshAsync(_listingId));
        Assert.False(await Refresher().RefreshAsync(_listingId)); // already current: no second fetch

        Assert.Equal(1, _source.Calls);
        Assert.Equal("White", Assert.Single((await Store.GetAsync(_listingId))!.Spec).Value);
    }

    [Fact]
    public async Task Unknown_listings_and_sources_cost_nothing()
    {
        Assert.False(await Refresher().RefreshAsync(Guid.NewGuid()));
        var other = (await new PostgresListingStore(pg.Db).UpsertAsync(Fixtures.Gtr() with { Key = new ListingKey("elsewhere", "9") })).ListingId;
        Assert.False(await Refresher().RefreshAsync(other));
        Assert.Equal(0, _source.Calls);
    }

    [Fact]
    public async Task A_lot_page_with_old_details_asks_for_a_refresh_and_still_renders()
    {
        await Store.SaveAsync(_listingId, Details(format: 0));
        await using var factory = Api();
        using var http = factory.CreateClient();

        var json = await http.GetFromJsonAsync<JsonElement>($"/api/lots/{_listingId}");

        Assert.Equal("1991 Nissan Skyline (BNR32)", json.GetProperty("title").GetString());
        Assert.Equal([_listingId], _requests.Asked);
    }

    [Fact]
    public async Task Current_details_ask_for_nothing()
    {
        await Store.SaveAsync(_listingId, Details(ListingDetails.CurrentFormat));
        await using var factory = Api();
        using var http = factory.CreateClient();

        await http.GetFromJsonAsync<JsonElement>($"/api/lots/{_listingId}");

        Assert.Empty(_requests.Asked);
    }

    private WebApplicationFactory<Mitsuke.Api.LotViewBuilder> Api() => new WebApplicationFactory<Mitsuke.Api.LotViewBuilder>().WithWebHostBuilder(web =>
    {
        web.UseEnvironment("Testing");
        web.UseSetting("MITSUKE_DB", "Host=unused");
        web.ConfigureServices(services =>
        {
            services.RemoveAll<NpgsqlDataSource>();
            services.AddSingleton(pg.Db);
            services.RemoveAll<IDetailsRefreshRequests>();
            services.AddSingleton<IDetailsRefreshRequests>(_requests);
        });
    });

    private sealed class FakeDetails : IListingDetailsSource
    {
        public int Calls { get; private set; }

        public bool CanFetch(ListingKey key) => key.Source == "fake";

        public Task<ListingDetails?> GetDetailsAsync(ListingKey key, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<ListingDetails?>(Details(ListingDetails.CurrentFormat));
        }
    }

    private sealed class RecordingRequests : IDetailsRefreshRequests
    {
        public List<Guid> Asked { get; } = [];

        public Task RequestAsync(Guid listingId, CancellationToken cancellationToken = default)
        {
            Asked.Add(listingId);
            return Task.CompletedTask;
        }
    }
}
