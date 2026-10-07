// Mitsuke's read API for the web app (web/), plus the one write: "I want to bid".
//
//   dotnet run --project src/Mitsuke.Api        # http://localhost:5107, OpenAPI at /openapi/v1.json
//
// The web app calls this from its server, never from the browser, so there's no CORS and no public surface
// beyond what the web app chooses to expose.

using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;
using Mitsuke.Api;
using Mitsuke.Core;
using Mitsuke.Data;
using Mitsuke.Notifications;
using Mitsuke.Pricing;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    DotEnv.LoadNearest();
    builder.Configuration.AddEnvironmentVariables(); // re-read so the .env values are visible
}

builder.Services.AddMitsukeData(builder.Configuration["MITSUKE_DB"] ?? "");
builder.Services.AddMitsukePricing();
builder.Services.AddMitsukeNotifications(builder.Configuration);
builder.Services.AddSingleton<LotViewBuilder>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Bid requests notify a human, so they're the one endpoint worth protecting from floods.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("bids", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10) }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

var api = app.MapGroup("/api");

api.MapGet("/health", () => TypedResults.Ok(new { status = "healthy" }));

api.MapGet("/lots/{id:guid}", async Task<Results<Ok<LotView>, NotFound>> (Guid id, string? to, LotViewBuilder lots, CancellationToken ct) =>
    await lots.BuildAsync(id, Destination(to), ct) is { } view ? TypedResults.Ok(view) : TypedResults.NotFound())
    .WithSummary("Everything the lot page shows: photos, landed cost, deal score, decoded sheet, history.");

api.MapGet("/matches", async (Guid? watchlistId, string? to, int? limit, LotViewBuilder lots, CancellationToken ct) =>
    TypedResults.Ok(await lots.CardsAsync(watchlistId, Destination(to), Math.Clamp(limit ?? 24, 1, 100), ct)))
    .WithSummary("Recently alerted lots, newest first.");

api.MapGet("/watchlists", async (IReadQueries queries, CancellationToken ct) =>
    TypedResults.Ok(await queries.GetWatchlistSummariesAsync(ct)))
    .WithSummary("Active watchlists with how many matches each has had.");

api.MapPost("/lots/{id:guid}/bid-requests",
    async Task<Results<Created<BidRequestCreated>, ValidationProblem, NotFound>> (
        Guid id, BidRequestBody body, IListingStore listings, IBidRequestStore bids, INotifier notifier, CancellationToken ct) =>
    {
        var errors = body.Validate();
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
        if (await listings.GetAsync(id, ct) is not { } listing) return TypedResults.NotFound();

        var request = new BidRequest(id, new Money(body.MaxBidJpy, "JPY"), body.Name.Trim(), body.Email.Trim(), body.Note?.Trim());
        var requestId = await bids.AddAsync(request, ct);

        // MVP hand-off: tell Nick, who forwards it to the partner exporter. Mitsuke never bids or holds money.
        await notifier.SendAsync(string.Create(CultureInfo.GetCultureInfo("en-AU"),
            $"BID REQUEST {requestId}\n{LotViewBuilder.Title(listing)}, {listing.AuctionHouse} lot {listing.LotNumber}\n" +
            $"Max bid: ¥{body.MaxBidJpy:N0}\nFrom: {request.Name} <{request.Email}>\n{request.Note}"), ct);

        return TypedResults.Created($"/api/bid-requests/{requestId}", new BidRequestCreated(requestId));
    })
    .RequireRateLimiting("bids")
    .WithSummary("Ask for a partner exporter to bid on this lot. Mitsuke never bids or takes payment.");

app.Run();

static string Destination(string? to) => string.IsNullOrWhiteSpace(to) ? "AU" : to.Trim().ToUpperInvariant();

