using Dapper;
using Mitsuke.Core;
using Npgsql;

namespace Mitsuke.Data;

public sealed class PostgresComparablesStore(NpgsqlDataSource db) : IComparablesStore
{
    public async Task<IReadOnlyList<Comparable>> GetCandidatesAsync(Listing subject, int yearWindow = 3, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        await using var conn = await db.OpenConnectionAsync(cancellationToken);

        // One row per physical car: listings linked to a vehicle collapse to the most recently seen one,
        // so a car relisted every week counts once. The subject car (by listing and by frame number) is excluded.
        var rows = await conn.QueryAsync<Row>("""
            select distinct on (coalesce(l.vehicle_id, l.id))
                   l.source as Source, l.source_id as SourceId, l.year as Year, l.mileage_km as MileageKm,
                   l.grade_score as GradeScore, l.grade_repaired as IsRepaired,
                   p.amount as Amount, p.currency as Currency
            from listings l
            join lateral (
                select amount, currency from price_observations po
                where po.listing_id = l.id
                order by po.observed_at desc, po.id desc
                limit 1
            ) p on true
            where lower(l.make) = lower(@Make)
              and lower(l.model) = lower(@Model)
              and (@ModelCode::text is null or l.model_code = @ModelCode)
              and (@Year::int is null or l.year between @Year - @yearWindow and @Year + @yearWindow)
              and not (l.source = @Source and l.source_id = @SourceId)
              and (@FrameNumber::text is null or l.frame_number is distinct from @FrameNumber)
            order by coalesce(l.vehicle_id, l.id), l.last_seen_at desc
            """, new
        {
            subject.Make,
            subject.Model,
            subject.ModelCode,
            subject.Year,
            yearWindow,
            subject.Key.Source,
            subject.Key.SourceId,
            subject.FrameNumber,
        });

        return rows.Select(r => new Comparable(
            new ListingKey(r.Source, r.SourceId), r.Year, r.MileageKm, r.GradeScore, r.IsRepaired, new Money(r.Amount, r.Currency.Trim())))
            .ToList();
    }

    private sealed record Row
    {
        public string Source { get; init; } = "";
        public string SourceId { get; init; } = "";
        public int? Year { get; init; }
        public int? MileageKm { get; init; }
        public decimal? GradeScore { get; init; }
        public bool IsRepaired { get; init; }
        public decimal Amount { get; init; }
        public string Currency { get; init; } = "";
    }
}
