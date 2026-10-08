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
    private readonly FakeDetails _details = new();
    private readonly FakeSheets _sheets = new();

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

    private Collector CreateCollector() => new(
        [_source], new PostgresListingStore(pg.Db), Landed.Estimator, new FakeTimeProvider(Now), NullLogger<Collector>.Instance);

    private AlertSender CreateSender() => new(
        new PostgresListingStore(pg.Db),
        new PostgresWatchlistStore(pg.Db),
        new PostgresAlertLog(pg.Db),
        [_details],
        new PostgresListingDetailsStore(pg.Db),
        Landed.Estimator,
        new PostgresComparablesStore(pg.Db),
        [_sheets], new PostgresSheetReportStore(pg.Db),
        _notifier,
        new FakeTimeProvider(Now),
        NullLogger<AlertSender>.Instance);

    private Scanner CreateScanner() => new(CreateCollector(), CreateSender(), new PostgresWatchlistStore(pg.Db), NullLogger<Scanner>.Instance);

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
    public async Task One_lot_listed_twice_is_alerted_once_through_its_priced_copy()
    {
        var unpriced = Lot("copy-a") with { LotNumber = "58212", Price = null };
        var priced = Lot("copy-b") with { LotNumber = "58212" };
        _source.Lots = [unpriced, priced];
        var watchlist = (await new PostgresWatchlistStore(pg.Db).GetActiveAsync()).Single();

        var result = await CreateCollector().CollectAsync(watchlist);

        Assert.Equal(2, result.Seen);                  // both stored...
        var match = Assert.Single(result.Matches);     // ...one alert
        Assert.Equal("copy-b", (await new PostgresListingStore(pg.Db).GetAsync(match.ListingId))!.Key.SourceId);
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

    [Fact]
    public async Task Details_are_fetched_once_per_new_alert_and_cached()
    {
        _source.Lots = [Lot("gtr-1"), Lot("gts-1", modelCode: "HCR32")];

        await CreateScanner().RunAsync();
        await CreateScanner().RunAsync();

        Assert.Equal(["gtr-1"], _details.Requested); // non-matches and repeat scans cost nothing
        Assert.Contains("⚠ Mileage marked as doubtful", _notifier.Sent[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_details_failure_still_sends_the_alert()
    {
        _source.Lots = [Lot("gtr-1")];
        _details.Fail = true;

        var result = await CreateScanner().RunAsync();

        Assert.Equal((1, 0), (result.Sent, result.Failed));
        Assert.Single(_details.Requested);
        Assert.DoesNotContain("Auction sheet", _notifier.Sent[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Collector_reports_every_match_and_the_sender_decides_what_is_new()
    {
        _source.Lots = [Lot("gtr-1")];
        var watchlist = (await new PostgresWatchlistStore(pg.Db).GetActiveAsync()).Single();

        var first = await CreateCollector().CollectAsync(watchlist);
        var again = await CreateCollector().CollectAsync(watchlist);

        // The same match is reported both times; only the sender's claim stops a second alert.
        Assert.Equal(first.Matches, again.Matches);
        Assert.Equal(AlertOutcome.Sent, await CreateSender().SendAsync(first.Matches[0]));
        Assert.Equal(AlertOutcome.AlreadySent, await CreateSender().SendAsync(again.Matches[0]));
        Assert.Single(_notifier.Sent);
    }

    [Fact]
    public async Task Sender_skips_stale_requests_for_deleted_listings_or_paused_watchlists()
    {
        _source.Lots = [Lot("gtr-1")];
        var watchlist = (await new PostgresWatchlistStore(pg.Db).GetActiveAsync()).Single();
        var match = (await CreateCollector().CollectAsync(watchlist)).Matches.Single();

        Assert.Equal(AlertOutcome.Skipped, await CreateSender().SendAsync(match with { ListingId = Guid.NewGuid() }));

        await using (var conn = await pg.Db.OpenConnectionAsync())
            await Dapper.SqlMapper.ExecuteAsync(conn, "update watchlists set is_active = false");
        Assert.Equal(AlertOutcome.Skipped, await CreateSender().SendAsync(match));
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public async Task Sender_rethrows_failures_so_a_queue_can_redeliver()
    {
        _source.Lots = [Lot("gtr-1")];
        _notifier.FailNext = 1;
        var watchlist = (await new PostgresWatchlistStore(pg.Db).GetActiveAsync()).Single();
        var match = (await CreateCollector().CollectAsync(watchlist)).Matches.Single();

        var thrown = await Assert.ThrowsAsync<AlertDeliveryException>(() => CreateSender().SendAsync(match));
        Assert.IsType<HttpRequestException>(thrown.InnerException);
        Assert.Equal(AlertOutcome.Sent, await CreateSender().SendAsync(match)); // redelivery succeeds
    }

    [Fact]
    public async Task The_sheet_is_decoded_once_and_its_red_flags_lead_the_alert()
    {
        _source.Lots = [Lot("gtr-1")];
        var watchlist = (await new PostgresWatchlistStore(pg.Db).GetActiveAsync()).Single();
        var match = (await CreateCollector().CollectAsync(watchlist)).Matches.Single();

        await CreateSender().SendAsync(match);
        await using (var conn = await pg.Db.OpenConnectionAsync())
            await Dapper.SqlMapper.ExecuteAsync(conn, "delete from alerts"); // force a second alert for the same car
        await CreateSender().SendAsync(match);

        Assert.Single(_sheets.Decoded); // paid for once, then served from sheet_reports
        var text = _notifier.Sent[^1];
        Assert.True(text.IndexOf("⚠ Mileage marked as doubtful", StringComparison.Ordinal) < text.IndexOf("Est. landed", StringComparison.Ordinal));
        Assert.Contains("Condition (from the auction sheet): Tidy for its age.", text, StringComparison.Ordinal);
        Assert.Contains("Also check: 6 aftermarket parts.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sheet_decoding_failure_still_sends_the_alert()
    {
        _source.Lots = [Lot("gtr-1")];
        _sheets.Fail = true;

        var result = await CreateScanner().RunAsync();

        Assert.Equal((1, 0), (result.Sent, result.Failed));
        Assert.Contains("Auction sheet available (not yet decoded).", _notifier.Sent[0], StringComparison.Ordinal);
    }

    private sealed class FakeSheets : ISheetDecoder
    {
        public List<Uri> Decoded { get; } = [];
        public bool Fail { get; set; }

        public Task<SheetReport?> DecodeAsync(Uri sheetUrl, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new HttpRequestException("Kensa-ya down");
            Decoded.Add(sheetUrl);
            return Task.FromResult<SheetReport?>(new SheetReport
            {
                SheetUrl = sheetUrl,
                DecodedAt = Now,
                IsAuctionSheet = true,
                Summary = "Tidy for its age.",
                RedFlags =
                [
                    new SheetFlag(FlagSeverity.High, "Mileage marked as doubtful", "The true distance is unknown."),
                    new SheetFlag(FlagSeverity.Medium, "6 aftermarket parts", "Check they're road legal."),
                ],
                CostUsd = 0.09m,
            });
        }
    }

    private sealed class FakeDetails : IListingDetailsSource
    {
        public List<string> Requested { get; } = [];
        public bool Fail { get; set; }

        public bool CanFetch(ListingKey key) => key.Source == "fake";

        public Task<ListingDetails?> GetDetailsAsync(ListingKey key, CancellationToken cancellationToken = default)
        {
            Requested.Add(key.SourceId);
            if (Fail) throw new HttpRequestException("detail endpoint down");
            return Task.FromResult<ListingDetails?>(new ListingDetails
            {
                Key = key,
                FetchedAt = Now,
                Sheets = [new AuctionSheet(new Uri("https://example.test/sheet.jpg"), null, IsCurrent: true)],
            });
        }
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
