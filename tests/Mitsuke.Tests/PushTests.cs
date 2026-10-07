using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mitsuke.Core;
using Mitsuke.Data;

namespace Mitsuke.Tests;

public class PushFormatterTests
{
    [Fact]
    public void Leads_with_serious_flags_and_keeps_it_short()
    {
        var landed = new LandedEstimate
        {
            Destination = "AU", Total = new Money(34_157m, "AUD"), Low = new Money(1, "AUD"), High = new Money(2, "AUD"), JpyPerUnit = 110,
        };
        var sheet = new SheetReport
        {
            SheetUrl = new Uri("https://x.test/s.jpg"), DecodedAt = DateTimeOffset.UnixEpoch, IsAuctionSheet = true, Summary = "",
            RedFlags = [new SheetFlag(FlagSeverity.High, "Mileage marked as doubtful", "x")],
        };
        var listing = Fixtures.Gtr() with { AuctionEndsAt = new DateTimeOffset(2026, 10, 8, 15, 0, 0, TimeSpan.Zero) };

        var m = PushFormatter.Format(listing, landed, null, sheet, new Uri("https://mitsuke.test/lot/1"));

        Assert.Equal("⚠ Found: 1991 Nissan Skyline (BNR32)", m.Title);
        Assert.Equal("⚠ Mileage marked as doubtful · Est. A$34,157 landed · 87,000 km (unverified) · Auction Thu 8 Oct", m.Body);
        Assert.Equal(new Uri("https://mitsuke.test/lot/1"), m.Url);
        Assert.Equal("lot-test/1", m.Tag); // one notification per car on the device
    }
}

[Collection(PostgresTests.Name)]
public sealed class PushDeliveryTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly FakePush _push = new();
    private readonly FakeEmail _email = new();
    private User _alice = null!;
    private Watchlist _mine = null!;
    private Guid _listingId;

    public async Task InitializeAsync()
    {
        await pg.ResetAsync();
        _alice = await new PostgresUserStore(pg.Db).EnsureAsync(Guid.NewGuid(), "alice@example.com");
        _mine = new Watchlist { Id = Guid.NewGuid(), Name = "Mine", Make = "Nissan", Model = "Skyline" };
        await new PostgresUserWatchlistStore(pg.Db, new PostgresWatchlistStore(pg.Db)).AddAsync(_alice.Id, _mine);
        _listingId = (await new PostgresListingStore(pg.Db).UpsertAsync(Fixtures.Gtr())).ListingId;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private PostgresPushSubscriptionStore Devices => new(pg.Db);

    private AlertSender Sender() => new(
        new PostgresListingStore(pg.Db), new PostgresWatchlistStore(pg.Db), new PostgresAlertLog(pg.Db), [], new PostgresListingDetailsStore(pg.Db),
        Landed.Estimator, new PostgresComparablesStore(pg.Db), [], new PostgresSheetReportStore(pg.Db), new Notifications.ConsoleNotifier(),
        new FakeTimeProvider(DateTimeOffset.UnixEpoch), NullLogger<AlertSender>.Instance,
        new AlertLinks(new Uri("https://mitsuke.test/")), _email, new PostgresUserStore(pg.Db), _push, Devices);

    [Fact]
    public async Task Own_watchlist_goes_to_email_and_every_device_once()
    {
        await Devices.SaveAsync(_alice.Id, "https://push.test/phone", "p", "a", "Android");
        await Devices.SaveAsync(_alice.Id, "https://push.test/laptop", "p", "a", "Windows");

        Assert.Equal(AlertOutcome.Sent, await Sender().SendAsync(new AlertRequest(_mine.Id, _listingId)));
        Assert.Equal(AlertOutcome.AlreadySent, await Sender().SendAsync(new AlertRequest(_mine.Id, _listingId)));

        Assert.Single(_email.Sent);
        Assert.Equal(["https://push.test/phone", "https://push.test/laptop"], _push.Sent.Select(s => s.Endpoint));
        Assert.Equal("https://mitsuke.test/lot/" + _listingId, _push.Messages[0].Url!.ToString());
    }

    [Fact]
    public async Task A_device_that_unsubscribed_is_removed_and_does_not_fail_the_alert()
    {
        await Devices.SaveAsync(_alice.Id, "https://push.test/gone", "p", "a", null);
        _push.GoneEndpoints.Add("https://push.test/gone");

        Assert.Equal(AlertOutcome.Sent, await Sender().SendAsync(new AlertRequest(_mine.Id, _listingId)));
        Assert.Empty(await Devices.GetForUserAsync(_alice.Id));
    }

    [Fact]
    public async Task A_push_failure_retries_only_the_push_never_the_email()
    {
        await Devices.SaveAsync(_alice.Id, "https://push.test/phone", "p", "a", null);
        _push.FailNext = 1;

        await Assert.ThrowsAsync<AlertDeliveryException>(() => Sender().SendAsync(new AlertRequest(_mine.Id, _listingId)));
        Assert.Single(_email.Sent);
        Assert.Empty(_push.Sent);

        Assert.Equal(AlertOutcome.Sent, await Sender().SendAsync(new AlertRequest(_mine.Id, _listingId))); // queue redelivery
        Assert.Single(_email.Sent); // not twice
        Assert.Single(_push.Sent);
    }

    [Fact]
    public async Task No_devices_means_email_only()
    {
        await Sender().SendAsync(new AlertRequest(_mine.Id, _listingId));
        Assert.Single(_email.Sent);
        await using var conn = await pg.Db.OpenConnectionAsync();
        Assert.Equal(["email"], await conn.QueryAsync<string>("select channel from alerts"));
    }

    [Fact]
    public async Task Subscriptions_belong_to_one_person_and_move_if_the_device_signs_in_as_someone_else()
    {
        var bob = await new PostgresUserStore(pg.Db).EnsureAsync(Guid.NewGuid(), "bob@example.com");
        await Devices.SaveAsync(_alice.Id, "https://push.test/shared", "p1", "a1", null);
        await Devices.SaveAsync(bob.Id, "https://push.test/shared", "p2", "a2", null);

        Assert.Empty(await Devices.GetForUserAsync(_alice.Id));
        Assert.Equal("p2", Assert.Single(await Devices.GetForUserAsync(bob.Id)).P256dh);

        await Devices.DeleteAsync(_alice.Id, "https://push.test/shared"); // Alice can't remove Bob's device
        Assert.Single(await Devices.GetForUserAsync(bob.Id));
    }

    private sealed class FakePush : IPushSender
    {
        public List<PushSubscription> Sent { get; } = [];
        public List<PushMessage> Messages { get; } = [];
        public HashSet<string> GoneEndpoints { get; } = [];
        public int FailNext { get; set; }

        public Task<PushResult> SendAsync(PushSubscription subscription, PushMessage message, CancellationToken cancellationToken = default)
        {
            if (FailNext-- > 0) throw new HttpRequestException("push service down");
            if (GoneEndpoints.Contains(subscription.Endpoint)) return Task.FromResult(PushResult.Gone);
            Sent.Add(subscription);
            Messages.Add(message);
            return Task.FromResult(PushResult.Delivered);
        }
    }

    private sealed class FakeEmail : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}

public class WebPushSenderTests
{
    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static (string Public, string Private) NewKeyPair()
    {
        using var ec = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(includePrivateParameters: true);
        return (B64Url([0x04, .. p.Q.X!, .. p.Q.Y!]), B64Url(p.D!));
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.Created, PushResult.Delivered)]
    [InlineData(System.Net.HttpStatusCode.Gone, PushResult.Gone)]
    [InlineData(System.Net.HttpStatusCode.NotFound, PushResult.Gone)]
    public async Task Sends_an_encrypted_vapid_signed_message(System.Net.HttpStatusCode status, PushResult expected)
    {
        var server = NewKeyPair();
        var device = NewKeyPair();
        var handler = new Capture(status);
        var sender = new Notifications.WebPushSender(new HttpClient(handler), new Notifications.VapidOptions
        {
            PublicKey = server.Public, PrivateKey = server.Private, Subject = "mailto:test@example.com",
        });

        var result = await sender.SendAsync(
            new PushSubscription(Guid.NewGuid(), Guid.NewGuid(), "https://push.example.test/abc", device.Public, B64Url(new byte[16])),
            new PushMessage("Found: 1991 Nissan Skyline", "Est. A$34,157 landed", new Uri("https://mitsuke.test/lot/1"), "lot-x"));

        Assert.Equal(expected, result);
        var req = handler.Request!;
        Assert.Equal("https://push.example.test/abc", req.RequestUri!.ToString());
        Assert.StartsWith("vapid t=", req.Headers.Authorization!.ToString(), StringComparison.Ordinal);
        Assert.Contains($"k={server.Public}", req.Headers.Authorization!.ToString(), StringComparison.Ordinal);
        Assert.Equal("aes128gcm", req.Content!.Headers.ContentEncoding.Single());
        Assert.Equal("high", req.Headers.GetValues("Urgency").Single());
        Assert.DoesNotContain("Skyline", System.Text.Encoding.UTF8.GetString(handler.Body!), StringComparison.Ordinal); // encrypted
    }

    private sealed class Capture(System.Net.HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public byte[]? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }
}
