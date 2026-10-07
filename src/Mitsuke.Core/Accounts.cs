namespace Mitsuke.Core;

public sealed record User(Guid Id, string Email);

public enum FeedbackKind
{
    Watching,
    NotForMe,
}

/// <summary>A person's opinion on a lot. "Not for me" hides it from their matches.</summary>
public sealed record LotFeedback(Guid ListingId, FeedbackKind Kind, string? Reason);

public interface IUserStore
{
    /// <summary>Creates the user on first sight (from a verified sign-in token) and keeps the email current.</summary>
    Task<User> EnsureAsync(Guid id, string email, CancellationToken cancellationToken = default);

    Task<User?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IUserWatchlistStore
{
    Task<IReadOnlyList<WatchlistSummary>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(Guid userId, Watchlist watchlist, CancellationToken cancellationToken = default);

    /// <summary>False when the watchlist doesn't exist or isn't this user's: callers answer 404 either way.</summary>
    Task<bool> SetActiveAsync(Guid userId, Guid watchlistId, bool isActive, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid userId, Guid watchlistId, CancellationToken cancellationToken = default);
}

public interface IFeedbackStore
{
    Task<LotFeedback?> GetAsync(Guid userId, Guid listingId, CancellationToken cancellationToken = default);

    Task SetAsync(Guid userId, LotFeedback feedback, CancellationToken cancellationToken = default);

    Task ClearAsync(Guid userId, Guid listingId, CancellationToken cancellationToken = default);
}
