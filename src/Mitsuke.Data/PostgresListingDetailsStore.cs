using System.Text.Json;
using Dapper;
using Mitsuke.Core;
using Npgsql;

namespace Mitsuke.Data;

public sealed class PostgresListingDetailsStore(NpgsqlDataSource db) : IListingDetailsStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ListingDetails?> GetAsync(Guid listingId, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var payload = await conn.QuerySingleOrDefaultAsync<string>(
            "select payload::text from listing_details where listing_id = @listingId", new { listingId });
        return payload is null ? null : JsonSerializer.Deserialize<ListingDetails>(payload, Json);
    }

    public async Task SaveAsync(Guid listingId, ListingDetails details, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync("""
            insert into listing_details (listing_id, fetched_at, sheet_count, relist_count, payload)
            values (@listingId, @fetchedAt, @sheets, @relists, @payload::jsonb)
            on conflict (listing_id) do update set
                fetched_at = excluded.fetched_at, sheet_count = excluded.sheet_count,
                relist_count = excluded.relist_count, payload = excluded.payload
            """, new
        {
            listingId,
            fetchedAt = details.FetchedAt.ToUniversalTime(),
            sheets = details.Sheets.Count,
            relists = details.Relists.Count,
            payload = JsonSerializer.Serialize(details, Json),
        });
    }
}
