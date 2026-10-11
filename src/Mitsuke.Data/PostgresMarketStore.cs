using Dapper;
using Mitsuke.Core;
using Npgsql;

namespace Mitsuke.Data;

public sealed class PostgresMarketStore(NpgsqlDataSource db) : IMarketStore
{
    // One physical car: the vehicle when the frame number is known, otherwise the lot itself (auction day, lot number,
    // chassis, year, mileage), so the feed's duplicate copies of a lot under two venue names count once.
    private const string CarKey = """
        coalesce(l.vehicle_id::text, concat_ws('|', (l.auction_ends_at at time zone 'Asia/Tokyo')::date, l.lot_number,
                 upper(l.model_code), l.year, l.mileage_km))
        """;

    private const string LatestYen = """
        join lateral (
            select amount, currency from price_observations po
            where po.listing_id = l.id
            order by po.observed_at desc, po.id desc
            limit 1
        ) p on p.currency = 'JPY' and p.amount > 0
        """;

    public async Task<IReadOnlyList<MarketSample>> GetSamplesAsync(MarketScope scope, DateTimeOffset since, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var codes = scope.ModelCodes.Select(c => c.ToUpperInvariant()).ToArray();
        var rows = await conn.QueryAsync<(DateTime Seen, decimal Amount, bool OnNow)>($"""
            select distinct on ({CarKey}, date_trunc('week', l.first_seen_at at time zone 'Asia/Tokyo'))
                   (l.first_seen_at at time zone 'Asia/Tokyo')::date as seen, p.amount,
                   (l.last_seen_at > @now - interval '2 days' and (l.auction_ends_at is null or l.auction_ends_at > @now)) as on_now
            from listings l
            {LatestYen}
            where lower(l.make) = lower(@make)
              and (case when cardinality(@codes) > 0 then upper(l.model_code) = any(@codes) else lower(l.model) = lower(@model) end)
              and (@yearFrom::int is null or l.year >= @yearFrom)
              and (@yearTo::int is null or l.year <= @yearTo)
              and l.first_seen_at >= @since
            order by {CarKey}, date_trunc('week', l.first_seen_at at time zone 'Asia/Tokyo'), l.last_seen_at desc
            """, new { make = scope.Make, model = scope.Model, codes = PgArray.Of(codes), yearFrom = scope.YearFrom, yearTo = scope.YearTo, since, now });
        return rows.Select(r => new MarketSample(DateOnly.FromDateTime(r.Seen), r.Amount, r.OnNow)).ToList();
    }

    public async Task<IReadOnlyList<MarketCandidate>> ScanAsync(DateTimeOffset since, DateTimeOffset now, int limit, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        // Typical per chassis code over twelve weeks, one row per car; then this period's cleanest cars that opened
        // at 85% of that or less. The 25% floor drops placeholder starts (a ¥10,000 "opening bid" is not a steal).
        var rows = await conn.QueryAsync<Row>($"""
            with cars as (
                select distinct on ({CarKey})
                       l.id, lower(l.make) as make, upper(l.model_code) as code, l.year, l.mileage_km, l.grade_score,
                       l.grade_repaired, l.first_seen_at, p.amount
                from listings l
                {LatestYen}
                where l.model_code is not null and l.first_seen_at >= @now - interval '84 days'
                order by {CarKey}, l.last_seen_at desc
            ),
            typical as (
                select make, code, count(*)::int as cars,
                       percentile_cont(0.5) within group (order by amount)::numeric as typical,
                       percentile_cont(0.5) within group (order by mileage_km) as km
                from cars group by make, code
                having count(*) >= 8
            )
            select c.id as ListingId, c.code as Grp, c.amount as Amount, t.typical as Typical, t.cars as Cars, c.first_seen_at as FirstSeenAt
            from cars c
            join typical t on t.make = c.make and t.code = c.code
            where c.first_seen_at >= @since
              and not c.grade_repaired and coalesce(c.grade_score, 0) >= 3.5
              and (c.mileage_km is null or t.km is null or c.mileage_km <= t.km * 1.25)
              and c.amount <= t.typical * 0.85 and c.amount >= t.typical * 0.25
            order by c.amount / t.typical, c.first_seen_at desc
            limit @limit
            """, new { since, now, limit });
        return rows.Select(r => new MarketCandidate(r.ListingId, r.Grp, r.Amount, r.Typical, r.Cars,
            new DateTimeOffset(DateTime.SpecifyKind(r.FirstSeenAt, DateTimeKind.Utc)))).ToList();
    }

    private sealed record Row
    {
        public Guid ListingId { get; init; }
        public string Grp { get; init; } = "";
        public decimal Amount { get; init; }
        public decimal Typical { get; init; }
        public int Cars { get; init; }
        public DateTime FirstSeenAt { get; init; }
    }
}
