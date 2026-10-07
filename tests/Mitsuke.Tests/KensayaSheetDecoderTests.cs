using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mitsuke.Core;
using Mitsuke.Data;
using Mitsuke.Kensaya;

namespace Mitsuke.Tests;

public class KensayaSheetDecoderTests
{
    private static readonly Uri Sheet = new("https://api.thecarapi.com/report-vault/japan/1/sheet.jpg");
    private static readonly string Key = new('k', 40);

    // Hand-written in the shape of Kensa-ya's /api/partner/sheet (ADR 0011), with extra fields Mitsuke must ignore.
    private const string Response = """
        {
          "extraction": {
            "document": { "is_auction_sheet": true, "issue": null, "auction_house": "USS Tokyo" },
            "vehicle": { "make": "Nissan", "colour": "Gun grey", "mileage_km": 67619, "something_new": 1 },
            "grades": { "overall": "3.5", "interior": "C", "exterior": null },
            "modifications": ["Aftermarket exhaust"],
            "positives": ["Manual"],
            "watch_out": ["Rust on side steps"],
            "unclear_fields": ["colour"],
            "summary_en": "Tired paint, odometer flagged."
          },
          "redFlags": [
            { "id": "mileage-doubtful", "severity": "high", "title": "Mileage marked as doubtful", "detail": "Ask for records." },
            { "id": "mods", "severity": "info", "title": "1 aftermarket part", "detail": "Check it." }
          ],
          "damage": [
            { "code": "U1", "location": "right_front_door", "locationLabel": "Right front door", "name": "Dent", "size": "small", "summary": "A dent.", "note": null },
            { "code": "Pハゲ", "location": "roof", "locationLabel": null, "name": null, "size": null, "summary": null, "note": null }
          ],
          "warnings": [],
          "model": "claude-opus-5-5",
          "costUsd": 0.09285
        }
        """;

    private static (ISheetDecoder Decoder, StubHandler Handler) Create(params Func<HttpResponseMessage>[] responses)
    {
        var handler = new StubHandler(responses);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["KENSAYA_BASE_URL"] = "https://kensa-ya.test",
            ["KENSAYA_PARTNER_KEY"] = Key,
        }).Build();
        var services = new ServiceCollection().AddLogging().AddKensayaSheetDecoding(config);
        services.AddHttpClient<ISheetDecoder, KensayaSheetDecoder>().ConfigurePrimaryHttpMessageHandler(() => handler);
        return (services.BuildServiceProvider().GetRequiredService<ISheetDecoder>(), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Sends_the_sheet_url_with_the_partner_key_and_maps_the_answer()
    {
        var (decoder, handler) = Create(() => Json(HttpStatusCode.OK, Response));

        var report = (await decoder.DecodeAsync(Sheet))!;

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://kensa-ya.test/api/partner/sheet", request.Uri.ToString());
        Assert.Equal($"Bearer {Key}", request.Authorization);
        Assert.Contains(Sheet.ToString(), request.Body, StringComparison.Ordinal);

        Assert.True(report.IsAuctionSheet);
        Assert.Equal("Tired paint, odometer flagged.", report.Summary);
        Assert.Equal(FlagSeverity.High, report.RedFlags[0].Severity);
        Assert.Equal(FlagSeverity.Info, report.RedFlags[1].Severity);
        Assert.Equal(("U1", "Right front door", "Dent"), (report.Damage[0].Code, report.Damage[0].Location, report.Damage[0].Name));
        Assert.Equal(("Pハゲ", "roof", null), (report.Damage[1].Code, report.Damage[1].Location, report.Damage[1].Name)); // untranslated code kept as-is
        Assert.Equal(("3.5", "C", "Gun grey", 67619), (report.OverallGrade, report.InteriorGrade, report.Colour, report.MileageKm));
        Assert.Equal(0.09285m, report.CostUsd);
    }

    [Fact]
    public async Task An_unreadable_sheet_is_an_answer_not_an_error()
    {
        var (decoder, handler) = Create(() => Json(HttpStatusCode.UnprocessableEntity, """{"error":"Could not read that sheet."}"""));
        Assert.Null(await decoder.DecodeAsync(Sheet));
        Assert.Single(handler.Requests); // not retried
    }

    [Fact]
    public async Task Retries_when_kensaya_is_temporarily_busy()
    {
        var (decoder, handler) = Create(
            () => Json(HttpStatusCode.ServiceUnavailable, """{"error":"busy"}"""),
            () => Json(HttpStatusCode.OK, Response));

        Assert.NotNull(await decoder.DecodeAsync(Sheet));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_bad_key_fails_loudly()
    {
        var (decoder, _) = Create(() => Json(HttpStatusCode.Unauthorized, """{"error":"Unauthorised."}"""));
        await Assert.ThrowsAsync<HttpRequestException>(() => decoder.DecodeAsync(Sheet));
    }

    [Fact]
    public void Not_configured_means_no_decoder_rather_than_a_broken_one()
    {
        var services = new ServiceCollection().AddKensayaSheetDecoding(new ConfigurationBuilder().Build());
        Assert.Null(services.BuildServiceProvider().GetService<ISheetDecoder>());
    }

    private sealed record Captured(Uri Uri, string? Authorization, string Body);

    private sealed class StubHandler(Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<Captured> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Captured(request.RequestUri!, request.Headers.Authorization?.ToString(),
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return responses[Math.Min(Requests.Count - 1, responses.Length - 1)]();
        }
    }
}

[Collection(PostgresTests.Name)]
public sealed class SheetReportStoreTests(PostgresFixture pg) : IAsyncLifetime
{
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Round_trips_and_counts_high_flags()
    {
        var listingId = (await new PostgresListingStore(pg.Db).UpsertAsync(Fixtures.Gtr())).ListingId;
        var store = new PostgresSheetReportStore(pg.Db);
        var report = new SheetReport
        {
            SheetUrl = new Uri("https://api.thecarapi.com/report-vault/japan/1/s.jpg"),
            DecodedAt = new DateTimeOffset(2026, 10, 7, 1, 0, 0, TimeSpan.Zero),
            IsAuctionSheet = true,
            Summary = "Tidy.",
            RedFlags = [new SheetFlag(FlagSeverity.High, "Mileage doubtful", "x"), new SheetFlag(FlagSeverity.Info, "Mods", "y")],
            Damage = [new SheetDamage("U1", "Right front door", "Dent", "small", "A dent.", null)],
            CostUsd = 0.09m,
        };

        Assert.Null(await store.GetAsync(listingId));
        await store.SaveAsync(listingId, report);

        var loaded = (await store.GetAsync(listingId))!;
        Assert.Equal(report.RedFlags, loaded.RedFlags);
        Assert.Equal(report.Damage, loaded.Damage);
        Assert.Equal((report.SheetUrl, report.Summary, report.DecodedAt), (loaded.SheetUrl, loaded.Summary, loaded.DecodedAt));

        await using var conn = await pg.Db.OpenConnectionAsync();
        Assert.Equal(1, await Dapper.SqlMapper.ExecuteScalarAsync<int>(conn, "select high_flags from sheet_reports"));
    }
}
