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

    private readonly FakeTimeProvider _clock = new(Now);

    private PipelineFunctions Functions(params Listing[] lots) => Functions(new OneShotSource(lots));

    private PipelineFunctions Functions(IListingSource source)
    {
        var watchlists = new PostgresWatchlistStore(pg.Db);
        var collector = new Collector([source], new PostgresListingStore(pg.Db), Landed.Estimator, new FakeTimeProvider(Now), NullLogger<Collector>.Instance);
        var sender = new AlertSender(new PostgresListingStore(pg.Db), watchlists, new PostgresAlertLog(pg.Db), [], new PostgresListingDetailsStore(pg.Db),
            Landed.Estimator, new PostgresComparablesStore(pg.Db),
            [], new PostgresSheetReportStore(pg.Db), new Notifications.ConsoleNotifier(), new FakeTimeProvider(Now), NullLogger<AlertSender>.Instance);
        return new PipelineFunctions(watchlists, collector, sender, _clock, NullLogger<PipelineFunctions>.Instance);
    }

    [Fact]
    public async Task Timer_fans_out_one_collect_message_per_active_watchlist()
    {
        await using (var conn = await pg.Db.OpenConnectionAsync())
            await conn.ExecuteAsync("update watchlists set is_active = false where name = 'second'");

        var messages = await Functions().ScheduleCollection(new TimerInfo(), CancellationToken.None);

        var request = JsonSerializer.Deserialize<CollectRequest>(Assert.Single(messages), PipelineFunctions.Json);
        Assert.Equal(_r32.Id, request!.WatchlistId);
        Assert.Equal(Now, request.ScheduledAt);
    }

    [Fact]
    public async Task Collect_emits_one_alert_message_per_match_in_the_shape_SendAlert_reads()
    {
        var lot = Fixtures.Gtr() with { Key = new ListingKey("fake", "1"), AuctionEndsAt = Now.AddDays(2) };
        var messages = await Functions(lot).CollectAsync(new CollectRequest(_r32.Id), 1, CancellationToken.None);

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

        Assert.Empty(await Functions(lot).CollectAsync(new CollectRequest(_r32.Id), 1, CancellationToken.None));
        Assert.Empty(await Functions(lot).CollectAsync(new CollectRequest(Guid.NewGuid()), 1, CancellationToken.None));
    }

    [Fact]
    public async Task A_collect_message_overtaken_by_a_newer_run_is_dropped_without_searching()
    {
        var source = new FailingSource();
        _clock.Advance(TimeSpan.FromMinutes(25)); // e.g. redelivered long after an outage

        Assert.Empty(await Functions(source).CollectAsync(new CollectRequest(_r32.Id, Now), 1, CancellationToken.None));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task A_failing_collect_is_retried_then_given_up_instead_of_dead_lettered()
    {
        var functions = Functions(new FailingSource());
        var request = new CollectRequest(_r32.Id, Now);

        // Earlier attempts throw so the queue redelivers...
        for (var attempt = 1; attempt < PipelineFunctions.MaxDequeueCount; attempt++)
            await Assert.ThrowsAsync<HttpRequestException>(() => functions.CollectAsync(request, attempt, CancellationToken.None));
        // ...and the last one completes the message: the next timer run covers it, so nothing goes to collect-poison.
        Assert.Empty(await functions.CollectAsync(request, PipelineFunctions.MaxDequeueCount, CancellationToken.None));
    }

    [Fact]
    public async Task Messages_from_before_the_timestamp_still_work() =>
        Assert.Empty(await Functions().CollectAsync(new CollectRequest(_r32.Id, ScheduledAt: null), 1, CancellationToken.None));

    [Fact]
    public void MaxDequeueCount_matches_host_json()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Mitsuke.slnx"))) root = Path.GetDirectoryName(root)!;
        using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "Mitsuke.Functions", "host.json")));
        Assert.Equal(PipelineFunctions.MaxDequeueCount, host.RootElement.GetProperty("extensions").GetProperty("queues").GetProperty("maxDequeueCount").GetInt32());
    }

    private sealed class FailingSource : IListingSource
    {
        public int Calls { get; private set; }
        public string Name => "fake";

        public async IAsyncEnumerable<Listing> SearchAsync(SourceQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Yield();
            throw new HttpRequestException("TheCarApi timed out");
#pragma warning disable CS0162 // an iterator needs a yield
            yield break;
#pragma warning restore CS0162
        }
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
