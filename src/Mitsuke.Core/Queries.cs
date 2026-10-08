namespace Mitsuke.Core;

public sealed record PricePoint(DateTimeOffset ObservedAt, Money Price, PriceKind Kind);

/// <summary>A listing that has been alerted on, newest first: what the home page shows.</summary>
public sealed record MatchSummary(Guid ListingId, Guid WatchlistId, string WatchlistName, DateTimeOffset AlertedAt);

public sealed record WatchlistSummary(Watchlist Watchlist, int MatchCount, DateTimeOffset? LastMatchAt);

/// <summary>Someone asking Mitsuke to have a partner exporter bid for them. Mitsuke itself never bids.</summary>
public sealed record BidRequest(Guid ListingId, Money MaxBid, string Name, string Email, string? Note);

/// <summary>Read-side queries for the web app, kept apart from the pipeline's write-side stores.</summary>
public interface IReadQueries
{
    Task<IReadOnlyList<PricePoint>> GetPriceHistoryAsync(Guid listingId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MatchSummary>> GetRecentMatchesAsync(Guid? watchlistId, int limit, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WatchlistSummary>> GetWatchlistSummariesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lots on auction now that a watchlist's search would turn up: same make, and its chassis codes (or model when it
    /// has none), seen in the last two days with the auction still ahead. Newest first, capped.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetCurrentLotsAsync(Watchlist watchlist, DateTimeOffset now, int limit, CancellationToken cancellationToken = default);

    /// <summary>Alerted lots from this user's watchlists, minus anything they marked "not for me".</summary>
    Task<IReadOnlyList<MatchSummary>> GetUserMatchesAsync(Guid userId, int limit, CancellationToken cancellationToken = default);
}

public interface IBidRequestStore
{
    Task<Guid> AddAsync(BidRequest request, CancellationToken cancellationToken = default);
}
