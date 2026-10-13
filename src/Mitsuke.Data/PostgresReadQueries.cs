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

    public async Task<LotSightings?> GetSightingsAsync(Guid listingId, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var me = await conn.QuerySingleOrDefaultAsync<(DateTime First, DateTime Last, bool HasChassis)>("""
            select first_seen_at, last_seen_at, (vehicle_id is not null or coalesce(frame_number, '') <> '')
            from listings where id = @listingId
            """, new { listingId });
        if (me == default) return null;

        // The same car is the same vehicle (frame number); a later auction day rules out the feed's same-day copies.
        var later = await conn.QueryAsync<(Guid Id, DateTime? EndsAt, string? House, string? Lot, decimal? Amount, string? Currency)>("""
            select l.id, l.auction_ends_at, l.auction_house, l.lot_number, p.amount, p.currency
            from listings me
            join listings l on l.id <> me.id
                and (l.vehicle_id = me.vehicle_id or (coalesce(me.frame_number, '') <> '' and l.frame_number = me.frame_number))
            left join lateral (
                select amount, currency from price_observations po
                where po.listing_id = l.id
                order by po.observed_at desc, po.id desc
                limit 1
            ) p on true
            where me.id = @listingId and l.auction_ends_at > me.auction_ends_at
            order by l.auction_ends_at
            limit 10
            """, new { listingId });

        static DateTimeOffset Utc(DateTime t) => new(DateTime.SpecifyKind(t, DateTimeKind.Utc));
        return new LotSightings(Utc(me.First), Utc(me.Last), me.HasChassis, later.Select(r => new LaterAppearance(
            r.Id,
            r.EndsAt is { } e ? DateOnly.FromDateTime(Utc(e).ToOffset(TimeSpan.FromHours(9)).AddSeconds(-1).DateTime) : null,
            r.House,
            r.Lot,
            r.Amount is { } a && r.Currency is { } c ? new Money(a, c.Trim()) : null)).ToList());
    }

    public async Task<IReadOnlyList<MatchSummary>> GetRecentMatchesAsync(Guid? watchlistId, int limit, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        // One row per (watchlist, listing) even if it went out on several channels.
        var rows = await conn.QueryAsync<(Guid ListingId, Guid WatchlistId, string Name, DateTime AlertedAt)>("""
            select a.listing_id, a.watchlist_id, w.name, min(coalesce(a.sent_at, a.created_at)) as alerted_at
            from alerts a
            join watchlists w on w.id = a.watchlist_id
            -- Public view: system/demo watchlists only. A user's matches come from GetUserMatchesAsync.
            where a.status = 'sent' and w.user_id is null
              and (@watchlistId::uuid is null or a.watchlist_id = @watchlistId)
            group by a.listing_id, a.watchlist_id, w.name
            order by alerted_at desc
            limit @limit
            """, new { watchlistId, limit });
        return rows.Select(r => new MatchSummary(r.ListingId, r.WatchlistId, r.Name, new DateTimeOffset(DateTime.SpecifyKind(r.AlertedAt, DateTimeKind.Utc)))).ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetCurrentLotsAsync(Watchlist watchlist, DateTimeOffset now, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var codes = watchlist.ModelCodes.Select(c => c.ToUpperInvariant()).ToArray();
        return (await conn.QueryAsync<Guid>("""
            select id from listings
            where lower(make) = lower(@make)
              and (case when cardinality(@codes) > 0 then upper(model_code) = any(@codes) else lower(model) = lower(@model) end)
              and last_seen_at > @now - interval '2 days'
              and (auction_ends_at is null or auction_ends_at > @now)
            order by last_seen_at desc
            limit @limit
            """, new { make = watchlist.Make, model = watchlist.Model, codes = PgArray.Of(codes), now, limit })).ToList();
    }

    public async Task<IReadOnlyList<MatchSummary>> GetUserMatchesAsync(Guid userId, int limit, Guid? watchlistId = null, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var rows = await conn.QueryAsync<(Guid ListingId, Guid WatchlistId, string Name, DateTime AlertedAt)>("""
            select a.listing_id, a.watchlist_id, w.name, min(coalesce(a.sent_at, a.created_at)) as alerted_at
            from alerts a
            join watchlists w on w.id = a.watchlist_id and w.user_id = @userId
            where a.status = 'sent'
              and (@watchlistId::uuid is null or a.watchlist_id = @watchlistId)
              and not exists (
                  select 1 from lot_feedback f
                  where f.user_id = @userId and f.listing_id = a.listing_id and f.kind = 'not_for_me')
            group by a.listing_id, a.watchlist_id, w.name
            order by alerted_at desc
            limit @limit
            """, new { userId, limit, watchlistId });
        return rows.Select(r => new MatchSummary(r.ListingId, r.WatchlistId, r.Name, new DateTimeOffset(DateTime.SpecifyKind(r.AlertedAt, DateTimeKind.Utc)))).ToList();
    }

    public async Task<IReadOnlyList<WatchlistSummary>> GetWatchlistSummariesAsync(CancellationToken cancellationToken = default)
    {
        var active = (await watchlists.GetActiveAsync(cancellationToken)).Where(w => w.OwnerId is null).ToList();
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var counts = (await conn.QueryAsync<(Guid WatchlistId, int Matches, DateTime Last)>("""
                -- Cars, not listings: one lot listed twice (ListingIdentity) counts once.
                select a.watchlist_id, count(distinct coalesce(
                case when l.lot_number <> '' and l.auction_ends_at is not null then concat_ws('|',
                    ((l.auction_ends_at at time zone 'Asia/Tokyo') - interval '1 second')::date, trim(l.lot_number),
                    upper(coalesce(l.model_code, '')), l.year, l.mileage_km) end,
                l.id::text))::int, max(coalesce(a.sent_at, a.created_at))
                from alerts a join listings l on l.id = a.listing_id
                where a.status = 'sent'
                group by a.watchlist_id
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
