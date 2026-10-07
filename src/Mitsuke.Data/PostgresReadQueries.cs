using Dapper;
using Mitsuke.Core;
using Npgsql;

namespace Mitsuke.Data;

public sealed class PostgresReadQueries(NpgsqlDataSource db, IWatchlistStore watchlists) : IReadQueries
{
    public async Task<IReadOnlyList<PricePoint>> GetPriceHistoryAsync(Guid listingId, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var rows = await conn.QueryAsync<(DateTime ObservedAt, decimal Amount, string Currency, string Kind)>("""
            select observed_at, amount, currency, kind from price_observations
            where listing_id = @listingId
            order by observed_at, id
            """, new { listingId });
        return rows.Select(r => new PricePoint(
            new DateTimeOffset(DateTime.SpecifyKind(r.ObservedAt, DateTimeKind.Utc)),
            new Money(r.Amount, r.Currency.Trim()),
            PostgresListingStore.FromDb(r.Kind))).ToList();
    }

    public async Task<IReadOnlyList<MatchSummary>> GetRecentMatchesAsync(Guid? watchlistId, int limit, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        // One row per (watchlist, listing) even if it went out on several channels.
        var rows = await conn.QueryAsync<(Guid ListingId, Guid WatchlistId, string Name, DateTime AlertedAt)>("""
            select a.listing_id, a.watchlist_id, w.name, min(coalesce(a.sent_at, a.created_at)) as alerted_at
            from alerts a
            join watchlists w on w.id = a.watchlist_id
            where a.status = 'sent' and (@watchlistId::uuid is null or a.watchlist_id = @watchlistId)
            group by a.listing_id, a.watchlist_id, w.name
            order by alerted_at desc
            limit @limit
            """, new { watchlistId, limit });
        return rows.Select(r => new MatchSummary(r.ListingId, r.WatchlistId, r.Name, new DateTimeOffset(DateTime.SpecifyKind(r.AlertedAt, DateTimeKind.Utc)))).ToList();
    }

    public async Task<IReadOnlyList<WatchlistSummary>> GetWatchlistSummariesAsync(CancellationToken cancellationToken = default)
    {
        var active = await watchlists.GetActiveAsync(cancellationToken);
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var counts = (await conn.QueryAsync<(Guid WatchlistId, int Matches, DateTime Last)>("""
                select watchlist_id, count(distinct listing_id)::int, max(coalesce(sent_at, created_at))
                from alerts where status = 'sent'
                group by watchlist_id
                """))
            .ToDictionary(r => r.WatchlistId);

        return active.Select(w => counts.TryGetValue(w.Id, out var c)
            ? new WatchlistSummary(w, c.Matches, new DateTimeOffset(DateTime.SpecifyKind(c.Last, DateTimeKind.Utc)))
            : new WatchlistSummary(w, 0, null)).ToList();
    }
}

public sealed class PostgresBidRequestStore(NpgsqlDataSource db) : IBidRequestStore
{
    public async Task<Guid> AddAsync(BidRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxBid.Currency != "JPY") throw new ArgumentException("Bids are placed in yen.", nameof(request));

        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        return await conn.ExecuteScalarAsync<Guid>("""
            insert into bid_requests (listing_id, max_bid_jpy, name, email, note)
            values (@ListingId, @amount, @Name, @Email, @Note)
            returning id
            """, new { request.ListingId, amount = request.MaxBid.Amount, request.Name, request.Email, request.Note });
    }
}
