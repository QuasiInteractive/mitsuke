using Microsoft.Extensions.Logging.Abstractions;
using Mitsuke.Core;
using Mitsuke.Data;

namespace Mitsuke.Tests;

public class CatalogNamingTests
{
    [Theory]
    [InlineData("LANCER EVOLUTION", "Lancer Evolution")]
    [InlineData("RX-7", "RX-7")]
    [InlineData("MARK II", "Mark II")]
    [InlineData("NSX", "NSX")]
    [InlineData("S2000", "S2000")]
    [InlineData("GT-R", "GT-R")]
    [InlineData("SKYLINE", "Skyline")]
    public void Model_labels_keep_acronyms_and_codes_upper_case(string raw, string label) => Assert.Equal(label, KnownGenerations.Label(raw));

    [Fact]
    public void Generations_merge_the_curated_list_with_codes_seen_in_real_lots()
    {
        var observed = new[]
        {
            new CatalogGeneration("BNR32", "BNR32", 1990, 1994, 7, false),
            new CatalogGeneration("ER33", "ER33", 1994, 1998, 2, false),      // not curated: kept, labelled by its code
        };

        var g = KnownGenerations.Merge("nissan", "SKYLINE", observed);

        var r32 = g.Single(x => x.Code == "BNR32");
        Assert.Equal(("R32 GT-R", 1989, 1994, 7, true), (r32.Label, r32.YearFrom, r32.YearTo, r32.Seen, r32.Curated)); // curated years win
        Assert.Contains(g, x => x.Code == "ER33" && !x.Curated);
        Assert.Contains(g, x => x.Code == "BNR34" && x.Seen == 0);                                                     // curated, not seen yet
        Assert.Equal("BNR34", g[0].Code);                                                                               // newest first
    }

    [Fact]
    public void Models_without_curated_generations_use_only_what_was_seen() =>
        Assert.Equal(["VB23"], KnownGenerations.Merge("BMW", "1 Series", [new CatalogGeneration("VB23", "VB23", 2005, 2008, 1, false)]).Select(g => g.Code));
}

[Collection(PostgresTests.Name)]
public sealed class CatalogStoreTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_sync_replaces_the_catalogue_and_keeps_a_makes_models_when_its_fetch_fails()
    {
        var store = new PostgresCatalogStore(pg.Db);
        var source = new FakeCatalog();
        var sync = new CatalogSync(source, store, NullLogger<CatalogSync>.Instance);

        Assert.Equal((2, 3), await sync.RunAsync());
        Assert.Equal(["Lancer", "Lancer Evolution"], (await store.GetModelsAsync("MITSUBISHI")).Select(m => m.Name));

        source.FailFor = "Nissan";
        source.Makes = [new CatalogMake("Mitsubishi", "mitsubishi", 1700), new CatalogMake("Nissan", "nissan", 4300)];
        await sync.RunAsync();
        Assert.Single(await store.GetModelsAsync("Nissan"));           // kept from the last good sync
        Assert.Equal(1700, (await store.GetMakesAsync()).Single(m => m.Name == "Mitsubishi").Lots);
    }

    [Fact]
    public async Task Observed_generations_come_from_the_listings_seen()
    {
        var listings = new PostgresListingStore(pg.Db);
        await listings.UpsertAsync(Fixtures.Gtr() with { Key = new ListingKey("t", "1"), Year = 1990 });
        await listings.UpsertAsync(Fixtures.Gtr() with { Key = new ListingKey("t", "2"), Year = 1993 });

        var g = Assert.Single(await new PostgresCatalogStore(pg.Db).GetObservedGenerationsAsync("nissan", "skyline"));
        Assert.Equal(("BNR32", 1990, 1993, 2), (g.Code, g.YearFrom, g.YearTo, g.Seen));
    }

    private sealed class FakeCatalog : ICatalogSource
    {
        public List<CatalogMake> Makes { get; set; } = [new("Mitsubishi", "mitsubishi", 1697), new("Nissan", "nissan", 4344), new("Gone", "gone", 0)];
        public string? FailFor { get; set; }

        public Task<IReadOnlyList<CatalogMake>> GetMakesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogMake>>(Makes);

        public Task<IReadOnlyList<CatalogModel>> GetModelsAsync(CatalogMake make, CancellationToken cancellationToken = default)
        {
            if (make.Name == FailFor) throw new HttpRequestException("down");
            IReadOnlyList<CatalogModel> models = make.Name == "Mitsubishi"
                ? [new("Mitsubishi", "Lancer Evolution", "Lancer Evolution", "lancer-evolution", 18), new("Mitsubishi", "Lancer", "Lancer", "lancer", 20)]
                : [new("Nissan", "Skyline", "Skyline", "skyline", 400)];
            return Task.FromResult(models);
        }
    }
}
