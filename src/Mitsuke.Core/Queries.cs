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

    /// <summary>Alerted lots from this user's watchlists, minus anything they marked "not for me".</summary>
    Task<IReadOnlyList<MatchSummary>> GetUserMatchesAsync(Guid userId, int limit, CancellationToken cancellationToken = default);
}

public interface IBidRequestStore
{
    Task<Guid> AddAsync(BidRequest request, CancellationToken cancellationToken = default);
}
