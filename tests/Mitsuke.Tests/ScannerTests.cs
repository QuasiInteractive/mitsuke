using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mitsuke.Core;
using Mitsuke.Data;

namespace Mitsuke.Tests;

/// <summary>The whole pass (fake source and notifier, real Postgres): the guarantees that make 24/7 polling safe.</summary>
[Collection(PostgresTests.Name)]
public sealed class ScannerTests(PostgresFixture pg) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 0, 0, TimeSpan.FromHours(9));

    private readonly FakeSource _source = new();
    private readonly FakeNotifier _notifier = new();

    public async Task InitializeAsync()
    {
        await pg.ResetAsync();
        await new PostgresWatchlistStore(pg.Db).AddAsync(new Watchlist
        {
            Id = Guid.NewGuid(),
            Name = "R32 GT-R",
            Make = "Nissan",
            Model = "Skyline",
            ModelCodes = new HashSet<string> { "BNR32" },
            MinGrade = 3.5m,
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Scanner CreateScanner() => new(
        [_source],
        new PostgresListingStore(pg.Db),
        new PostgresWatchlistStore(pg.Db),
        new PostgresAlertLog(pg.Db),
        _notifier,
        new FakeTimeProvider(Now),
        NullLogger<Scanner>.Instance);

    private static Listing Lot(string id, string modelCode = "BNR32") => Fixtures.Gtr(b => b.ModelCode = modelCode) with
    {
        Key = new ListingKey("fake", id),
        AuctionEndsAt = Now.AddDays(3),
        Attribution = "test auction",
    };

    [Fact]
    public async Task Alerts_matches_once_across_repeated_scans()
    {
        _source.Lots = [Lot("gtr-1"), Lot("gts-1", modelCode: "HCR32")];

        var first = await CreateScanner().RunAsync();
        var second = await CreateScanner().RunAsync();

        Assert.Equal(new ScanResult(Watchlists: 1, Seen: 2, New: 2, Matched: 1, Sent: 1, Failed: 0, AlreadyAlerted: 0), first);
        Assert.Equal(new ScanResult(Watchlists: 1, Seen: 2, New: 0, Matched: 1, Sent: 0, Failed: 0, AlreadyAlerted: 1), second);
        Assert.Single(_notifier.Sent);
        Assert.Contains("[R32 GT-R]", _notifier.Sent[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_match_on_a_later_scan_is_alerted()
    {
        _source.Lots = [Lot("gtr-1")];
        await CreateScanner().RunAsync();

        _source.Lots = [Lot("gtr-1"), Lot("gtr-2")];
        var result = await CreateScanner().RunAsync();

        Assert.Equal(1, result.Sent);
        Assert.Equal(2, _notifier.Sent.Count);
    }

    [Fact]
    public async Task A_failed_send_does_not_stop_the_scan_and_is_retried_next_time()
    {
        _source.Lots = [Lot("gtr-1"), Lot("gtr-2")];
        _notifier.FailNext = 1;

        var first = await CreateScanner().RunAsync();
        Assert.Equal((1, 1), (first.Sent, first.Failed));

        var second = await CreateScanner().RunAsync();
        Assert.Equal((1, 0, 1), (second.Sent, second.Failed, second.AlreadyAlerted));
        Assert.Equal(2, _notifier.Sent.Count); // each car alerted exactly once in the end
    }

    private sealed class FakeSource : IListingSource
    {
        public List<Listing> Lots { get; set; } = [];
        public string Name => "fake";

        public async IAsyncEnumerable<Listing> SearchAsync(SourceQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var lot in Lots)
            {
                await Task.Yield();
                yield return lot;
            }
        }
    }

    private sealed class FakeNotifier : INotifier
    {
        public List<string> Sent { get; } = [];
        public int FailNext { get; set; }
        public string Channel => "fake";

        public Task SendAsync(string message, CancellationToken cancellationToken = default)
        {
            if (FailNext-- > 0) throw new HttpRequestException("webhook down");
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
