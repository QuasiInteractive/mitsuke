using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Mitsuke.Core;
using Mitsuke.Data;
using Npgsql;

namespace Mitsuke.Tests;

/// <summary>
/// /api/me with real JWT validation. Tokens are ES256-signed by a key made here (as Supabase signs its own),
/// so the tests prove the API checks signatures, issuer, audience and expiry, and scopes data to the caller.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class MeApiTests(PostgresFixture pg) : IAsyncLifetime
{
    private const string Issuer = "https://auth.test/auth/v1";
    private static readonly ECDsaSecurityKey SigningKey = new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = "test" };
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private WebApplicationFactory<Mitsuke.Api.LotViewBuilder> _factory = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        await pg.ResetAsync();
        _factory = new WebApplicationFactory<Mitsuke.Api.LotViewBuilder>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Testing");
            web.UseSetting("MITSUKE_DB", "Host=unused");
            web.UseSetting("SUPABASE_URL", "https://auth.test");
            web.ConfigureServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>();
                services.AddSingleton(pg.Db);
                // Same validation as production, but with a known key instead of fetching the issuer's JWKS.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    var config = new OpenIdConnectConfiguration { Issuer = Issuer };
                    config.SigningKeys.Add(SigningKey);
                    o.Configuration = config;
                    // The handler already built a manager that would fetch keys from the issuer; swap in a fixed one.
                    o.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<OpenIdConnectConfiguration>(config);
                    o.TokenValidationParameters.ValidIssuer = Issuer;
                });
            });
        });
        _http = _factory.CreateClient();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static string Token(Guid sub, string email, string issuer = Issuer, string audience = "authenticated", SecurityKey? key = null, TimeSpan? lifetime = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", sub.ToString()), new Claim("email", email)]),
            NotBefore = DateTime.UtcNow.AddMinutes(-5),
            Expires = DateTime.UtcNow + (lifetime ?? TimeSpan.FromHours(1)),
            IssuedAt = DateTime.UtcNow.AddMinutes(-5),
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.EcdsaSha256),
        });

    private static HttpRequestMessage As(Guid user, HttpMethod method, string url, object? body = null)
    {
        var req = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(user, user == Alice ? "alice@example.com" : "bob@example.com"));
        return req;
    }

    private static object R32(string name = "R32 GT-R") => new
    {
        name, make = "Nissan", model = "Skyline", modelCodes = new[] { "bnr32" }, yearFrom = 1989, yearTo = 1994,
        maxMileageKm = 150_000, minGrade = 3.5, includeRepaired = false, destination = "AU", maxLandedAmount = 45_000,
    };

    private async Task<Guid> CreateAsync(Guid user, object body)
    {
        var res = await _http.SendAsync(As(user, HttpMethod.Post, "/api/me/watchlists", body));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Me_requires_a_valid_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync("/api/me")).StatusCode);

        async Task<HttpStatusCode> With(string token)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/me");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return (await _http.SendAsync(req)).StatusCode;
        }

        var forgedKey = new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = "test" };
        Assert.Equal(HttpStatusCode.Unauthorized, await With(Token(Alice, "a@x.com", key: forgedKey)));            // wrong signature
        Assert.Equal(HttpStatusCode.Unauthorized, await With(Token(Alice, "a@x.com", issuer: "https://evil.test")));  // wrong issuer
        Assert.Equal(HttpStatusCode.Unauthorized, await With(Token(Alice, "a@x.com", audience: "anon")));            // wrong audience
        Assert.Equal(HttpStatusCode.Unauthorized, await With(Token(Alice, "a@x.com", lifetime: TimeSpan.FromMinutes(-2)))); // expired beyond the 30 s clock skew
        Assert.Equal(HttpStatusCode.OK, await With(Token(Alice, "a@x.com")));
    }

    [Fact]
    public async Task Creating_a_watchlist_registers_the_user_and_normalises_input()
    {
        var id = await CreateAsync(Alice, R32());

        var lists = await (await _http.SendAsync(As(Alice, HttpMethod.Get, "/api/me/watchlists"))).Content.ReadFromJsonAsync<JsonElement>();
        var w = Assert.Single(lists.EnumerateArray()).GetProperty("watchlist");
        Assert.Equal(id, w.GetProperty("id").GetGuid());
        Assert.Equal("BNR32", w.GetProperty("modelCodes")[0].GetString());
        Assert.Equal("AUD", w.GetProperty("maxLanded").GetProperty("currency").GetString());
        Assert.Equal(Alice, w.GetProperty("ownerId").GetGuid());
    }

    [Fact]
    public async Task People_never_see_or_change_each_others_watchlists()
    {
        var alices = await CreateAsync(Alice, R32());

        var bobsView = await (await _http.SendAsync(As(Bob, HttpMethod.Get, "/api/me/watchlists"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, bobsView.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(As(Bob, HttpMethod.Patch, $"/api/me/watchlists/{alices}", new { isActive = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(As(Bob, HttpMethod.Delete, $"/api/me/watchlists/{alices}"))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _http.SendAsync(As(Alice, HttpMethod.Patch, $"/api/me/watchlists/{alices}", new { isActive = false }))).StatusCode);
        Assert.Empty(await new PostgresWatchlistStore(pg.Db).GetActiveAsync()); // paused = not collected
        Assert.Equal(HttpStatusCode.NoContent, (await _http.SendAsync(As(Alice, HttpMethod.Delete, $"/api/me/watchlists/{alices}"))).StatusCode);
    }

    private static object Device(string endpoint) => new { endpoint, keys = new { p256dh = "BPublicKey", auth = "authSecret" } };

    [Fact]
    public async Task Devices_are_saved_per_person_and_validated()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await _http.SendAsync(As(Alice, HttpMethod.Put, "/api/me/push-subscriptions", Device("https://push.test/a")))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _http.SendAsync(As(Alice, HttpMethod.Put, "/api/me/push-subscriptions", Device("https://push.test/a")))).StatusCode); // idempotent
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.SendAsync(As(Alice, HttpMethod.Put, "/api/me/push-subscriptions", Device("http://169.254.169.254/")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.SendAsync(As(Alice, HttpMethod.Put, "/api/me/push-subscriptions", new { endpoint = "https://push.test/b" }))).StatusCode);

        var devices = new PostgresPushSubscriptionStore(pg.Db);
        await _http.SendAsync(As(Bob, HttpMethod.Post, "/api/me/push-subscriptions/remove", new { endpoint = "https://push.test/a" })); // not Bob's
        Assert.Single(await devices.GetForUserAsync(Alice));

        await _http.SendAsync(As(Alice, HttpMethod.Post, "/api/me/push-subscriptions/remove", new { endpoint = "https://push.test/a" }));
        Assert.Empty(await devices.GetForUserAsync(Alice));
    }

    [Fact]
    public async Task Test_push_goes_to_my_devices_only()
    {
        var push = new RecordingPush();
        using var factory = _factory.WithWebHostBuilder(web => web.ConfigureServices(s => s.AddSingleton<IPushSender>(push)));
        using var http = factory.CreateClient();
        await http.SendAsync(As(Alice, HttpMethod.Put, "/api/me/push-subscriptions", Device("https://push.test/alice")));
        await http.SendAsync(As(Bob, HttpMethod.Put, "/api/me/push-subscriptions", Device("https://push.test/bob")));

        var res = await http.SendAsync(As(Alice, HttpMethod.Post, "/api/me/push-subscriptions/test"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(1, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("delivered").GetInt32());
        Assert.Equal(["https://push.test/alice"], push.Endpoints);
    }

    [Fact]
    public async Task Test_push_says_so_when_the_server_has_no_keys() =>
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _http.SendAsync(As(Alice, HttpMethod.Post, "/api/me/push-subscriptions/test"))).StatusCode);

    private sealed class RecordingPush : IPushSender
    {
        public List<string> Endpoints { get; } = [];

        public Task<PushResult> SendAsync(PushSubscription subscription, PushMessage message, CancellationToken cancellationToken = default)
        {
            Endpoints.Add(subscription.Endpoint);
            return Task.FromResult(PushResult.Delivered);
        }
    }

    [Fact]
    public async Task Bad_watchlists_get_field_errors()
    {
        var res = await _http.SendAsync(As(Alice, HttpMethod.Post, "/api/me/watchlists",
            new { name = "", make = "Nissan", model = "Skyline", yearFrom = 1995, yearTo = 1990, minGrade = 9, destination = "US", maxLandedAmount = 5 }));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        foreach (var field in new[] { "name", "yearFrom", "minGrade", "destination", "maxLandedAmount" })
            Assert.True(errors.TryGetProperty(field, out _), field);
    }

    [Fact]
    public async Task Each_person_is_capped_at_ten_watchlists()
    {
        for (var i = 0; i < Mitsuke.Api.MeEndpoints.MaxWatchlistsPerUser; i++) await CreateAsync(Alice, R32($"list {i}"));
        var res = await _http.SendAsync(As(Alice, HttpMethod.Post, "/api/me/watchlists", R32("one too many")));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Not_for_me_hides_a_lot_from_my_matches_only()
    {
        var watchlistId = await CreateAsync(Alice, R32());
        var listingId = (await new PostgresListingStore(pg.Db).UpsertAsync(Fixtures.Gtr())).ListingId;
        var alerts = new PostgresAlertLog(pg.Db);
        await alerts.MarkSentAsync((await alerts.TryClaimAsync(watchlistId, listingId, "test"))!.Value);

        async Task<int> MatchCount() =>
            (await (await _http.SendAsync(As(Alice, HttpMethod.Get, "/api/me/matches"))).Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength();

        Assert.Equal(1, await MatchCount());
        Assert.Equal(HttpStatusCode.NoContent,
            (await _http.SendAsync(As(Alice, HttpMethod.Put, $"/api/me/lots/{listingId}/feedback", new { kind = "NotForMe", reason = "Too many dents" }))).StatusCode);
        Assert.Equal(0, await MatchCount());

        var feedback = await (await _http.SendAsync(As(Alice, HttpMethod.Get, $"/api/me/lots/{listingId}/feedback"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NotForMe", feedback.GetProperty("kind").GetString());

        // Bob has no opinion on it, and Alice's private watchlist never shows on the public matches.
        Assert.Equal(HttpStatusCode.NoContent, (await _http.SendAsync(As(Bob, HttpMethod.Get, $"/api/me/lots/{listingId}/feedback"))).StatusCode);
        Assert.Equal(0, (await _http.GetFromJsonAsync<JsonElement>("/api/matches")).GetArrayLength());

        await _http.SendAsync(As(Alice, HttpMethod.Delete, $"/api/me/lots/{listingId}/feedback"));
        Assert.Equal(1, await MatchCount());
    }
}
