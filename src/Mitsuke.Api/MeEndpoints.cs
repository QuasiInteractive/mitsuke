using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Mitsuke.Core;

namespace Mitsuke.Api;

/// <summary>Everything under /api/me: the signed-in person's watchlists, matches and lot feedback.</summary>
public static class MeEndpoints
{
    /// <summary>Per-person cap: every watchlist is another search against TheCarApi every ten minutes.</summary>
    public const int MaxWatchlistsPerUser = 10;

    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").RequireAuthorization();

        me.MapGet("/", async (ClaimsPrincipal principal, IUserStore users, CancellationToken ct) =>
            TypedResults.Ok(await CurrentUserAsync(principal, users, ct)));

        me.MapGet("/watchlists", async (ClaimsPrincipal principal, IUserStore users, IUserWatchlistStore watchlists, CancellationToken ct) =>
        {
            var user = await CurrentUserAsync(principal, users, ct);
            return TypedResults.Ok(await watchlists.GetForUserAsync(user.Id, ct));
        });

        me.MapPost("/watchlists", async Task<Results<Created<WatchlistCreated>, ValidationProblem>> (
            CreateWatchlistBody body, ClaimsPrincipal principal, IUserStore users, IUserWatchlistStore watchlists, CancellationToken ct) =>
        {
            var errors = body.Validate();
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

            var user = await CurrentUserAsync(principal, users, ct);
            if ((await watchlists.GetForUserAsync(user.Id, ct)).Count >= MaxWatchlistsPerUser)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [""] = [$"You can have up to {MaxWatchlistsPerUser} watchlists. Delete one to add another."],
                });

            var watchlist = body.ToWatchlist();
            await watchlists.AddAsync(user.Id, watchlist, ct);
            return TypedResults.Created($"/api/me/watchlists/{watchlist.Id}", new WatchlistCreated(watchlist.Id));
        });

        me.MapPatch("/watchlists/{id:guid}", async Task<Results<NoContent, NotFound>> (
            Guid id, UpdateWatchlistBody body, ClaimsPrincipal principal, IUserStore users, IUserWatchlistStore watchlists, CancellationToken ct) =>
        {
            var user = await CurrentUserAsync(principal, users, ct);
            return await watchlists.SetActiveAsync(user.Id, id, body.IsActive, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
        });

        me.MapDelete("/watchlists/{id:guid}", async Task<Results<NoContent, NotFound>> (
            Guid id, ClaimsPrincipal principal, IUserStore users, IUserWatchlistStore watchlists, CancellationToken ct) =>
        {
            var user = await CurrentUserAsync(principal, users, ct);
            return await watchlists.DeleteAsync(user.Id, id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
        });

        me.MapGet("/matches", async (string? to, ClaimsPrincipal principal, IUserStore users, IReadQueries queries, LotViewBuilder lots, CancellationToken ct) =>
        {
            var user = await CurrentUserAsync(principal, users, ct);
            var matches = await queries.GetUserMatchesAsync(user.Id, 48, ct);
            return TypedResults.Ok(await lots.CardsAsync(matches, Destination(to), ct));
        });

        me.MapGet("/lots/{id:guid}/feedback", async Task<Results<Ok<LotFeedback>, NoContent>> (
            Guid id, ClaimsPrincipal principal, IUserStore users, IFeedbackStore feedback, CancellationToken ct) =>
        {
            var user = await CurrentUserAsync(principal, users, ct);
            return await feedback.GetAsync(user.Id, id, ct) is { } f ? TypedResults.Ok(f) : TypedResults.NoContent();
        });

        me.MapPut("/lots/{id:guid}/feedback", async Task<Results<NoContent, ValidationProblem, NotFound>> (
            Guid id, FeedbackBody body, ClaimsPrincipal principal, IUserStore users, IListingStore listings, IFeedbackStore feedback, CancellationToken ct) =>
        {
            if (body.Reason is { Length: > 200 })
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Keep the reason under 200 characters."] });
            if (await listings.GetAsync(id, ct) is null) return TypedResults.NotFound();

            var user = await CurrentUserAsync(principal, users, ct);
            await feedback.SetAsync(user.Id, new LotFeedback(id, body.Kind, body.Reason?.Trim()), ct);
            return TypedResults.NoContent();
        });

        me.MapDelete("/lots/{id:guid}/feedback", async (Guid id, ClaimsPrincipal principal, IUserStore users, IFeedbackStore feedback, CancellationToken ct) =>
        {
            var user = await CurrentUserAsync(principal, users, ct);
            await feedback.ClearAsync(user.Id, id, ct);
            return TypedResults.NoContent();
        });

        // Devices with notifications on. The browser's PushManager gives the endpoint and keys; the endpoint is the
        // push service's URL (Google, Apple, Mozilla), so only https is accepted, and the keys are only used to encrypt.
        me.MapPut("/push-subscriptions", async Task<Results<NoContent, ValidationProblem>> (
            PushSubscriptionBody body, HttpRequest http, ClaimsPrincipal principal, IUserStore users, IPushSubscriptionStore devices, CancellationToken ct) =>
        {
            var errors = body.Validate();
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

            var user = await CurrentUserAsync(principal, users, ct);
            var agent = http.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua[..Math.Min(ua.Length, 300)] : null;
            await devices.SaveAsync(user.Id, body.Endpoint, body.Keys.P256dh, body.Keys.Auth, agent, ct);
            return TypedResults.NoContent();
        });

        me.MapPost("/push-subscriptions/remove", async (
            PushEndpointBody body, ClaimsPrincipal principal, IUserStore users, IPushSubscriptionStore devices, CancellationToken ct) =>
        {
            var user = await CurrentUserAsync(principal, users, ct);
            await devices.DeleteAsync(user.Id, body.Endpoint, ct);
            return TypedResults.NoContent();
        });

        // "Send me a test": proves the whole chain (keys, service worker, OS permission) before a real car turns up.
        me.MapPost("/push-subscriptions/test", async Task<Results<Ok<PushTestResult>, ProblemHttpResult>> (
            ClaimsPrincipal principal, IUserStore users, IPushSubscriptionStore devices, [FromServices] IPushSender? push, IConfiguration config, CancellationToken ct) =>
        {
            if (push is null) return TypedResults.Problem("Push notifications aren't configured on the server.", statusCode: 503);

            var user = await CurrentUserAsync(principal, users, ct);
            var webUrl = Uri.TryCreate(config["MITSUKE_WEB_URL"], UriKind.Absolute, out var w) ? w : null;
            var message = new PushMessage("Mitsuke notifications are on", "We'll ping this device the moment a car matches your watchlist.", webUrl, "mitsuke-test");
            int delivered = 0, gone = 0;
            foreach (var device in await devices.GetForUserAsync(user.Id, ct))
            {
                if (await push.SendAsync(device, message, ct) == PushResult.Gone)
                {
                    await devices.RemoveAsync(device.Id, ct);
                    gone++;
                }
                else
                {
                    await devices.MarkDeliveredAsync(device.Id, ct);
                    delivered++;
                }
            }
            return TypedResults.Ok(new PushTestResult(delivered, gone));
        }).RequireRateLimiting("bids");

        return api;
    }

    /// <summary>
    /// The person behind a verified token. The token's signature, issuer, audience and expiry were checked by
    /// the JWT middleware; this only reads its subject and email and makes sure Mitsuke has a row for them.
    /// </summary>
    private static async Task<User> CurrentUserAsync(ClaimsPrincipal principal, IUserStore users, CancellationToken ct)
    {
        var sub = principal.FindFirstValue("sub");
        var email = principal.FindFirstValue("email") ?? "";
        if (!Guid.TryParse(sub, out var id)) throw new UnauthorizedAccessException("Token has no usable subject.");
        return await users.EnsureAsync(id, email, ct);
    }

    internal static string Destination(string? to) => string.IsNullOrWhiteSpace(to) ? "AU" : to.Trim().ToUpperInvariant();
}

public sealed record WatchlistCreated(Guid Id);

public sealed record PushKeys(string P256dh, string Auth);

public sealed record PushSubscriptionBody(string Endpoint, PushKeys Keys)
{
    public Dictionary<string, string[]> Validate()
    {
        var e = new Dictionary<string, string[]>();
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || Endpoint.Length > 1000)
            e["endpoint"] = ["Not a push subscription endpoint."];
        if (Keys is null || string.IsNullOrWhiteSpace(Keys.P256dh) || Keys.P256dh.Length > 200 || string.IsNullOrWhiteSpace(Keys.Auth) || Keys.Auth.Length > 100)
            e["keys"] = ["Missing the subscription's keys."];
        return e;
    }
}

public sealed record PushEndpointBody(string Endpoint);

public sealed record PushTestResult(int Delivered, int Removed);

public sealed record UpdateWatchlistBody(bool IsActive);

public sealed record FeedbackBody(FeedbackKind Kind, string? Reason);

public sealed record CreateWatchlistBody(
    string Name,
    string Make,
    string Model,
    string[]? ModelCodes,
    int? YearFrom,
    int? YearTo,
    int? MaxMileageKm,
    decimal? MinGrade,
    bool IncludeRepaired,
    string? Destination,
    decimal? MaxLandedAmount)
{
    private static readonly string[] Destinations = ["AU", "NZ"];

    public Dictionary<string, string[]> Validate()
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 60) e["name"] = ["Give it a name (up to 60 characters)."];
        if (string.IsNullOrWhiteSpace(Make) || Make.Trim().Length > 40) e["make"] = ["Enter the make, e.g. Nissan."];
        if (string.IsNullOrWhiteSpace(Model) || Model.Trim().Length > 40) e["model"] = ["Enter the model, e.g. Skyline."];
        if (ModelCodes is { Length: > 10 } || ModelCodes?.Any(c => string.IsNullOrWhiteSpace(c) || c.Trim().Length > 20) == true)
            e["modelCodes"] = ["Up to 10 chassis codes, e.g. BNR32."];
        if (YearFrom is < 1950 or > 2035 || YearTo is < 1950 or > 2035) e["yearFrom"] = ["Years must be between 1950 and 2035."];
        else if (YearFrom > YearTo) e["yearFrom"] = ["'From' year must be before 'to' year."];
        if (MaxMileageKm is < 1 or > 1_000_000) e["maxMileageKm"] = ["Mileage must be between 1 and 1,000,000 km."];
        if (MinGrade is < 1 or > 6) e["minGrade"] = ["Auction grades run from 1 to 6."];
        if (Destination is not null && !Destinations.Contains(Destination.Trim().ToUpperInvariant())) e["destination"] = ["Choose Australia or New Zealand."];
        if (MaxLandedAmount is < 1_000 or > 10_000_000) e["maxLandedAmount"] = ["Budget must be between 1,000 and 10,000,000."];
        return e;
    }

    public Watchlist ToWatchlist()
    {
        var destination = (Destination ?? "AU").Trim().ToUpperInvariant();
        return new Watchlist
        {
            Id = Guid.NewGuid(),
            Name = Name.Trim(),
            Make = Make.Trim(),
            Model = Model.Trim(),
            ModelCodes = (ModelCodes ?? []).Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase),
            YearFrom = YearFrom,
            YearTo = YearTo,
            MaxMileageKm = MaxMileageKm,
            MinGrade = MinGrade,
            IncludeRepaired = IncludeRepaired,
            Destination = destination,
            MaxLanded = MaxLandedAmount is { } amount ? new Money(amount, destination == "NZ" ? "NZD" : "AUD") : null,
        };
    }
}
