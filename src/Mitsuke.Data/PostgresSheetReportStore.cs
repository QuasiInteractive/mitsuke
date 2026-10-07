using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Mitsuke.Core;
using Npgsql;

namespace Mitsuke.Data;

public sealed class PostgresSheetReportStore(NpgsqlDataSource db) : ISheetReportStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<SheetReport?> GetAsync(Guid listingId, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var payload = await conn.QuerySingleOrDefaultAsync<string>(
            "select payload::text from sheet_reports where listing_id = @listingId", new { listingId });
        return payload is null ? null : JsonSerializer.Deserialize<SheetReport>(payload, Json);
    }

    public async Task SaveAsync(Guid listingId, SheetReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync("""
            insert into sheet_reports (listing_id, sheet_url, decoded_at, model, cost_usd, high_flags, payload)
            values (@listingId, @sheetUrl, @decodedAt, @Model, @CostUsd, @highFlags, @payload::jsonb)
            on conflict (listing_id) do update set
                sheet_url = excluded.sheet_url, decoded_at = excluded.decoded_at, model = excluded.model,
                cost_usd = sheet_reports.cost_usd + excluded.cost_usd, high_flags = excluded.high_flags, payload = excluded.payload
            """, new
        {
            listingId,
            sheetUrl = report.SheetUrl.ToString(),
            decodedAt = report.DecodedAt.ToUniversalTime(),
            report.Model,
            report.CostUsd,
            highFlags = report.RedFlags.Count(f => f.Severity == FlagSeverity.High),
            payload = JsonSerializer.Serialize(report, Json),
        });
    }
}
