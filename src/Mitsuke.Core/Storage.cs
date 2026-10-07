namespace Mitsuke.Core;

/// <param name="ListingId">Our id for the listing (stable across scans).</param>
/// <param name="IsNew">First time we've seen this listing.</param>
/// <param name="PriceChanged">A new price observation was recorded (always true for a new listing with a price).</param>
public sealed record UpsertResult(Guid ListingId, bool IsNew, bool PriceChanged);

public interface IListingStore
{
    /// <summary>Insert or refresh a listing, link it to its vehicle, and record its price if it changed.</summary>
    Task<UpsertResult> UpsertAsync(Listing listing, CancellationToken cancellationToken = default);

    /// <summary>The listing as last seen, with its latest price. Null if unknown.</summary>
    Task<Listing?> GetAsync(Guid listingId, CancellationToken cancellationToken = default);
}

public interface IWatchlistStore
{
    Task<IReadOnlyList<Watchlist>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Any watchlist by id, active or not. Null if it doesn't exist.</summary>
    Task<Watchlist?> GetAsync(Guid watchlistId, CancellationToken cancellationToken = default);

    Task AddAsync(Watchlist watchlist, CancellationToken cancellationToken = default);
}

/// <summary>
/// Guarantees each (watchlist, listing, channel) alert is delivered at most once, while letting failed
/// sends be retried. Claim before sending, then mark the outcome.
/// </summary>
public interface IAlertLog
{
    /// <summary>
    /// Returns an alert id to send under, or null if this alert was already sent or is in flight.
    /// A failed alert, or one stuck in flight longer than the claim expiry (its sender died), can be claimed again.
    /// </summary>
    Task<long?> TryClaimAsync(Guid watchlistId, Guid listingId, string channel, CancellationToken cancellationToken = default);

    Task MarkSentAsync(long alertId, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(long alertId, string reason, CancellationToken cancellationToken = default);
}
