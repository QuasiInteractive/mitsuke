using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mitsuke.Core;
using Mitsuke.Data;

namespace Mitsuke.Tests;

public class AlertEmailFormatterTests
{
    private static readonly Watchlist R32 = new() { Id = Guid.NewGuid(), Name = "R32 GT-R", Make = "Nissan", Model = "Skyline" };

    private static SheetReport Sheet(string summary, params SheetFlag[] flags) => new()
    {
        SheetUrl = new Uri("https://api.thecarapi.com/report-vault/japan/1/s.jpg"),
        DecodedAt = DateTimeOffset.UnixEpoch,
        IsAuctionSheet = true,
        Summary = summary,
        RedFlags = flags,
    };

    [Fact]
    public void Escapes_everything_that_came_from_outside()
    {
        var hostile = Sheet("<script>alert(1)</script> Tidy <b>car</b>", new SheetFlag(FlagSeverity.High, "<img src=x onerror=1>", "\"quoted\""));
        var listing = Fixtures.Gtr() with { Attribution = "<a href='evil'>USS</a>" };

        var email = AlertEmailFormatter.Format("buyer@example.com", R32 with { Name = "<i>mine</i>" }, listing, null, null, null, hostile, null, null);

        Assert.DoesNotContain("<script>", email.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", email.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<a href='evil'>", email.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<i>mine</i>", email.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", email.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Subject_flags_serious_problems_and_leads_with_the_landed_cost()
    {
        var landed = new LandedEstimate
        {
            Destination = "AU",
            Total = new Money(34_157m, "AUD"),
            Low = new Money(28_357m, "AUD"),
            High = new Money(39_957m, "AUD"),
            JpyPerUnit = 110,
        };
        var flagged = Sheet("Odometer tampered.", new SheetFlag(FlagSeverity.High, "Mileage marked as doubtful", "Unknown mileage."));

        var email = AlertEmailFormatter.Format("buyer@example.com", R32, Fixtures.Gtr(), null, landed, null, flagged,
            new Uri("https://mitsuke.test/lot/1"), new Uri("https://mitsuke.test/watchlists"));

        Assert.Equal("⚠ Found: 1991 Nissan Skyline (BNR32) · est. A$34,157 landed", email.Subject);
        Assert.Contains("87,000 km (unverified)", email.Html, StringComparison.Ordinal); // odometer flagged: never shown as fact
        Assert.Contains("87,000 km (unverified)", email.Text, StringComparison.Ordinal);
        Assert.Contains("Mileage marked as doubtful", email.Html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://mitsuke.test/lot/1\"", email.Html, StringComparison.Ordinal);
        Assert.Contains("Pause or change your watchlists", email.Html, StringComparison.Ordinal);
        Assert.Contains("View in Mitsuke → https://mitsuke.test/lot/1", email.Text, StringComparison.Ordinal); // plain-text part
        Assert.Equal("buyer@example.com", email.To);
    }

    [Fact]
    public void A_clean_car_has_no_warning_in_the_subject()
    {
        var email = AlertEmailFormatter.Format("b@example.com", R32, Fixtures.Gtr(), null, null, null, null, null, null);
        Assert.StartsWith("Found: ", email.Subject, StringComparison.Ordinal);
    }
}

/// <summary>Who gets which channel: people's own watchlists by email, system watchlists to the shared notifier.</summary>
[Collection(PostgresTests.Name)]
public sealed class AlertRoutingTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CapturingEmail _email = new();
    private readonly CapturingNotifier _notifier = new();

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private AlertSender Sender() => new(
        new PostgresListingStore(pg.Db), new PostgresWatchlistStore(pg.Db), new PostgresAlertLog(pg.Db), [], new PostgresListingDetailsStore(pg.Db),
        Landed.Estimator, new PostgresComparablesStore(pg.Db), [], new PostgresSheetReportStore(pg.Db), _notifier,
        new FakeTimeProvider(DateTimeOffset.UnixEpoch), NullLogger<AlertSender>.Instance,
        new AlertLinks(new Uri("https://mitsuke.test/")), _email, new PostgresUserStore(pg.Db));

    [Fact]
    public async Task Own_watchlists_are_emailed_to_their_owner_once_and_system_ones_go_to_the_shared_channel()
    {
        var alice = await new PostgresUserStore(pg.Db).EnsureAsync(Guid.NewGuid(), "alice@example.com");
        var mine = new Watchlist { Id = Guid.NewGuid(), Name = "Mine", Make = "Nissan", Model = "Skyline" };
        var demo = new Watchlist { Id = Guid.NewGuid(), Name = "Demo", Make = "Nissan", Model = "Skyline" };
        await new PostgresUserWatchlistStore(pg.Db, new PostgresWatchlistStore(pg.Db)).AddAsync(alice.Id, mine);
        await new PostgresWatchlistStore(pg.Db).AddAsync(demo);
        var listingId = (await new PostgresListingStore(pg.Db).UpsertAsync(Fixtures.Gtr())).ListingId;

        Assert.Equal(AlertOutcome.Sent, await Sender().SendAsync(new AlertRequest(mine.Id, listingId)));
        Assert.Equal(AlertOutcome.AlreadySent, await Sender().SendAsync(new AlertRequest(mine.Id, listingId)));
        Assert.Equal(AlertOutcome.Sent, await Sender().SendAsync(new AlertRequest(demo.Id, listingId)));

        var email = Assert.Single(_email.Sent);
        Assert.Equal("alice@example.com", email.To);
        Assert.Contains("https://mitsuke.test/lot/", email.Html, StringComparison.Ordinal);
        Assert.Contains("[Demo]", Assert.Single(_notifier.Sent), StringComparison.Ordinal);
    }

    private sealed class CapturingEmail : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
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
