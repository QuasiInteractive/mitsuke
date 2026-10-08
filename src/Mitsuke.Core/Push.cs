using System.Globalization;

namespace Mitsuke.Core;

/// <summary>A device that agreed to receive notifications. Keys come from the browser's PushManager subscription.</summary>
public sealed record PushSubscription(Guid Id, Guid UserId, string Endpoint, string P256dh, string Auth);

/// <summary>What the service worker shows. Kept short: phones truncate long notifications.</summary>
public sealed record PushMessage(string Title, string Body, Uri? Url, string Tag);

public enum PushResult
{
    Delivered,

    /// <summary>The push service says the subscription no longer exists (uninstalled, permission revoked): delete it.</summary>
    Gone,
}

public interface IPushSender
{
    Task<PushResult> SendAsync(PushSubscription subscription, PushMessage message, CancellationToken cancellationToken = default);
}

public interface IPushSubscriptionStore
{
    Task<IReadOnlyList<PushSubscription>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Adds or re-points a device (the same endpoint signing in as someone else moves to them).</summary>
    Task SaveAsync(Guid userId, string endpoint, string p256dh, string auth, string? userAgent, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid userId, string endpoint, CancellationToken cancellationToken = default);

    Task MarkDeliveredAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
}

public static class PushFormatter
{
    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");

    public static PushMessage Format(Listing listing, LandedEstimate? landed, DealScore? deal, SheetReport? sheet, Uri? lotUrl, ImportEligibility? import = null)
    {
        ArgumentNullException.ThrowIfNull(listing);
        var title = string.Join(' ', new[] { listing.Year?.ToString(CultureInfo.InvariantCulture), listing.Make, listing.Model }.Where(s => !string.IsNullOrEmpty(s)))
            + (listing.ModelCode is { } c ? $" ({c})" : "");
        var serious = sheet?.RedFlags.FirstOrDefault(f => f.Severity == FlagSeverity.High);

        var parts = new List<string>();
        if (serious is not null) parts.Add($"⚠ {serious.Title}");
        if (landed is not null) parts.Add(string.Create(Au, $"Est. {(landed.Total.Currency == "NZD" ? "NZ$" : "A$")}{landed.Total.Amount:N0} landed"));
        // An importable car needs no mention here; anything else is worth knowing before tapping.
        if (import is { Verdict: not EligibilityVerdict.Yes }) parts.Add($"Import: {char.ToLowerInvariant(import.Headline[0])}{import.Headline[1..]}");
        if (deal is { Score: { } score }) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{score}/100 {deal.Label}"));
        if (listing.MileageKm is { } km) parts.Add(string.Create(Au, $"{km:N0} km{(sheet?.MileageIsDoubtful == true ? " (unverified)" : "")}"));
        if (AlertFormatter.AuctionDay(listing) is { } day) parts.Add(string.Create(Au, $"Auction {day:ddd d MMM}"));

        // The tag collapses repeats for the same car into one notification on the device.
        return new PushMessage((serious is null ? "Found: " : "⚠ Found: ") + title, string.Join(" · ", parts), lotUrl, $"lot-{listing.Key}");
    }
}
