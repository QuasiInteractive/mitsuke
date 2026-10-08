using Microsoft.Extensions.Logging;

namespace Mitsuke.Core;

/// <summary>Asks for a listing's details to be fetched again in the background (the API's side of the "details" queue).</summary>
public interface IDetailsRefreshRequests
{
    Task RequestAsync(Guid listingId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Brings a listing's saved details up to the current <see cref="ListingDetails.CurrentFormat"/>, so lots alerted before
/// a new field existed (the spec list, the gearbox) get it too. Runs in the pipeline, the one place with the provider's
/// key and rate limiter: lot page views only queue a request. One fetch per car per format, never per page view.
/// </summary>
public sealed partial class DetailsRefresher(
    IListingStore listings,
    IEnumerable<IListingDetailsSource> sources,
    IListingDetailsStore store,
    ILogger<DetailsRefresher> logger)
{
    public static bool IsCurrent(ListingDetails? details) => details is { Format: >= ListingDetails.CurrentFormat };

    /// <returns>True when fresh details were fetched and saved.</returns>
    public async Task<bool> RefreshAsync(Guid listingId, CancellationToken cancellationToken = default)
    {
        if (IsCurrent(await store.GetAsync(listingId, cancellationToken))) return false; // someone else's request got there first
        if (await listings.GetAsync(listingId, cancellationToken) is not { } listing) return false;
        if (sources.FirstOrDefault(s => s.CanFetch(listing.Key)) is not { } source) return false;

        // Failures throw, so the queue retries (and parks the message in details-poison after five tries).
        if (await source.GetDetailsAsync(listing.Key, cancellationToken) is not { } fresh) return false;
        await store.SaveAsync(listingId, fresh, cancellationToken);
        LogRefreshed(logger, listing.Key, fresh.Spec.Count);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Refreshed details for {Key}: {SpecItems} spec items")]
    private static partial void LogRefreshed(ILogger logger, ListingKey key, int specItems);
}
