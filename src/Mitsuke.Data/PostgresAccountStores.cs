using Dapper;
using Mitsuke.Core;
using Npgsql;

namespace Mitsuke.Data;

public sealed class PostgresUserStore(NpgsqlDataSource db) : IUserStore
{
    public async Task<User> EnsureAsync(Guid id, string email, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync("""
            insert into users (id, email) values (@id, @email)
            on conflict (id) do update set email = excluded.email where users.email <> excluded.email
            """, new { id, email });
        return new User(id, email);
    }

    public async Task<User?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var email = await conn.QuerySingleOrDefaultAsync<string>("select email from users where id = @id", new { id });
        return email is null ? null : new User(id, email);
    }
}

/// <summary>Every query is scoped by user_id, so one person can never read or change another's watchlists.</summary>
public sealed class PostgresUserWatchlistStore(NpgsqlDataSource db, IWatchlistStore watchlists) : IUserWatchlistStore
{
    public async Task<IReadOnlyList<WatchlistSummary>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var rows = await conn.QueryAsync<(Guid Id, int Matches, DateTime? Last)>("""
            select w.id, count(distinct a.listing_id)::int, max(coalesce(a.sent_at, a.created_at))
            from watchlists w
            left join alerts a on a.watchlist_id = w.id and a.status = 'sent'
            where w.user_id = @userId
            group by w.id, w.created_at
            order by w.created_at
            """, new { userId });

        var result = new List<WatchlistSummary>();
        foreach (var row in rows)
        {
            if (await watchlists.GetAsync(row.Id, cancellationToken) is not { } watchlist) continue;
            result.Add(new WatchlistSummary(watchlist, row.Matches,
                row.Last is { } last ? new DateTimeOffset(DateTime.SpecifyKind(last, DateTimeKind.Utc)) : null));
        }
        return result;
    }

    public async Task AddAsync(Guid userId, Watchlist watchlist, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        // Owner set in the same insert: there's never a moment when the watchlist exists without its owner.
        await watchlists.AddAsync(watchlist with { OwnerId = userId }, cancellationToken);
    }

    public async Task<bool> SetActiveAsync(Guid userId, Guid watchlistId, bool isActive, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        return await conn.ExecuteAsync(
            "update watchlists set is_active = @isActive where id = @watchlistId and user_id = @userId",
            new { userId, watchlistId, isActive }) == 1;
    }

    public async Task<bool> UpdateAsync(Guid userId, Watchlist watchlist, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        return await conn.ExecuteAsync("""
            update watchlists set
                name = @Name, make = @Make, model = @Model, model_codes = @ModelCodes, year_from = @YearFrom, year_to = @YearTo,
                max_mileage_km = @MaxMileageKm, min_grade = @MinGrade, include_repaired = @IncludeRepaired,
                destination = @Destination, max_landed_amount = @MaxLandedAmount, max_landed_currency = @MaxLandedCurrency,
                max_price_amount = @MaxPriceAmount, max_price_currency = @MaxPriceCurrency
            where id = @Id and user_id = @userId
            """, new
        {
            userId,
            watchlist.Id,
            watchlist.Name,
            watchlist.Make,
            watchlist.Model,
            ModelCodes = PgArray.Of(watchlist.ModelCodes),
            watchlist.YearFrom,
            watchlist.YearTo,
            watchlist.MaxMileageKm,
            watchlist.MinGrade,
            watchlist.IncludeRepaired,
            watchlist.Destination,
            MaxLandedAmount = watchlist.MaxLanded?.Amount,
            MaxLandedCurrency = watchlist.MaxLanded?.Currency,
            MaxPriceAmount = watchlist.MaxPrice?.Amount,
            MaxPriceCurrency = watchlist.MaxPrice?.Currency,
        }) == 1;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid watchlistId, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        return await conn.ExecuteAsync(
            "delete from watchlists where id = @watchlistId and user_id = @userId", new { userId, watchlistId }) == 1;
    }
}

public sealed class PostgresFeedbackStore(NpgsqlDataSource db) : IFeedbackStore
{
    public async Task<LotFeedback?> GetAsync(Guid userId, Guid listingId, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var row = await conn.QuerySingleOrDefaultAsync<(string Kind, string? Reason)?>(
            "select kind, reason from lot_feedback where user_id = @userId and listing_id = @listingId", new { userId, listingId });
        return row is { } r ? new LotFeedback(listingId, r.Kind == "not_for_me" ? FeedbackKind.NotForMe : FeedbackKind.Watching, r.Reason) : null;
    }

    public async Task SetAsync(Guid userId, LotFeedback feedback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync("""
            insert into lot_feedback (user_id, listing_id, kind, reason)
            values (@userId, @ListingId, @kind, @Reason)
            on conflict (user_id, listing_id) do update set kind = excluded.kind, reason = excluded.reason, created_at = now()
            """, new { userId, feedback.ListingId, kind = feedback.Kind == FeedbackKind.NotForMe ? "not_for_me" : "watching", feedback.Reason });
    }

    public async Task ClearAsync(Guid userId, Guid listingId, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync("delete from lot_feedback where user_id = @userId and listing_id = @listingId", new { userId, listingId });
    }
}
