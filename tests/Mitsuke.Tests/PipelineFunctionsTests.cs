using System.Runtime.CompilerServices;
using System.Text.Json;
using Dapper;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mitsuke.Core;
using Mitsuke.Data;
using Mitsuke.Functions;

namespace Mitsuke.Tests;

/// <summary>The queue wiring: what each function emits, so the stages agree on message shapes.</summary>
[Collection(PostgresTests.Name)]
public sealed class PipelineFunctionsTests(PostgresFixture pg) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 0, 0, TimeSpan.FromHours(9));
    private readonly Watchlist _r32 = new() { Id = Guid.NewGuid(), Name = "R32", Make = "Nissan", Model = "Skyline", ModelCodes = new HashSet<string> { "BNR32" } };

    public async Task InitializeAsync()
    {
        await pg.ResetAsync();
        await new PostgresWatchlistStore(pg.Db).AddAsync(_r32);
        await new PostgresWatchlistStore(pg.Db).AddAsync(_r32 with { Id = Guid.NewGuid(), Name = "second" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private PipelineFunctions Functions(params Listing[] lots)
    {
        var watchlists = new PostgresWatchlistStore(pg.Db);
        var collector = new Collector([new OneShotSource(lots)], new PostgresListingStore(pg.Db), Landed.Estimator, new FakeTimeProvider(Now), NullLogger<Collector>.Instance);
        var sender = new AlertSender(new PostgresListingStore(pg.Db), watchlists, new PostgresAlertLog(pg.Db), [], new PostgresListingDetailsStore(pg.Db),
            Landed.Estimator, new PostgresComparablesStore(pg.Db), new Notifications.ConsoleNotifier(), new FakeTimeProvider(Now), NullLogger<AlertSender>.Instance);
        return new PipelineFunctions(watchlists, collector, sender, NullLogger<PipelineFunctions>.Instance);
    }

    [Fact]
    public async Task Timer_fans_out_one_collect_message_per_active_watchlist()
    {
        await using (var conn = await pg.Db.OpenConnectionAsync())
            await conn.ExecuteAsync("update watchlists set is_active = false where name = 'second'");

        var messages = await Functions().ScheduleCollection(new TimerInfo(), CancellationToken.None);

        var request = JsonSerializer.Deserialize<CollectRequest>(Assert.Single(messages), PipelineFunctions.Json);
        Assert.Equal(_r32.Id, request!.WatchlistId);
    }

    [Fact]
    public async Task Collect_emits_one_alert_message_per_match_in_the_shape_SendAlert_reads()
    {
        var lot = Fixtures.Gtr() with { Key = new ListingKey("fake", "1"), AuctionEndsAt = Now.AddDays(2) };
        var messages = await Functions(lot).Collect(new CollectRequest(_r32.Id), CancellationToken.None);

        var request = JsonSerializer.Deserialize<AlertRequest>(Assert.Single(messages), PipelineFunctions.Json)!;
        Assert.Equal(_r32.Id, request.WatchlistId);
        Assert.NotEqual(Guid.Empty, request.ListingId);
    }

    [Fact]
    public async Task Collect_does_nothing_for_a_paused_or_deleted_watchlist()
    {
        var lot = Fixtures.Gtr() with { Key = new ListingKey("fake", "1"), AuctionEndsAt = Now.AddDays(2) };
        await using (var conn = await pg.Db.OpenConnectionAsync())
            await conn.ExecuteAsync("update watchlists set is_active = false");

        Assert.Empty(await Functions(lot).Collect(new CollectRequest(_r32.Id), CancellationToken.None));
        Assert.Empty(await Functions(lot).Collect(new CollectRequest(Guid.NewGuid()), CancellationToken.None));
    }

    private sealed class OneShotSource(Listing[] lots) : IListingSource
    {
        public string Name => "fake";

        public async IAsyncEnumerable<Listing> SearchAsync(SourceQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var lot in lots)
            {
                await Task.Yield();
                yield return lot;
            }
        }
    }
}
