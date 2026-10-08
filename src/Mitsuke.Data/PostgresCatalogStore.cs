using Dapper;
using Mitsuke.Core;
using Npgsql;

namespace Mitsuke.Data;

public sealed class PostgresCatalogStore(NpgsqlDataSource db) : ICatalogStore
{
    public async Task ReplaceAsync(IReadOnlyList<CatalogMake> makes, IReadOnlyList<CatalogModel> models, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(makes);
        ArgumentNullException.ThrowIfNull(models);
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        await conn.ExecuteAsync("delete from catalog_makes", transaction: tx); // cascades to models
        await conn.ExecuteAsync(
            "insert into catalog_makes (name, slug, lots) values (@Name, @Slug, @Lots) on conflict (name) do nothing", makes, tx);
        var known = makes.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        await conn.ExecuteAsync(
            "insert into catalog_models (make, name, label, slug, lots) values (@Make, @Name, @Label, @Slug, @Lots) on conflict (make, name) do update set lots = catalog_models.lots + excluded.lots",
            models.Where(m => known.Contains(m.Make)), tx);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogMake>> GetMakesAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        return (await conn.QueryAsync<CatalogMake>("select name as Name, slug as Slug, lots as Lots from catalog_makes order by name")).ToList();
    }

    public async Task<IReadOnlyList<CatalogModel>> GetModelsAsync(string make, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        return (await conn.QueryAsync<CatalogModel>("""
            select make as Make, name as Name, label as Label, slug as Slug, lots as Lots
            from catalog_models where lower(make) = lower(@make) order by label
            """, new { make })).ToList();
    }

    public async Task<IReadOnlyList<CatalogGeneration>> GetObservedGenerationsAsync(string make, string model, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var rows = await conn.QueryAsync<(string Code, int? From, int? To, int Seen)>("""
            select model_code, min(year), max(year), count(*)::int
            from listings
            where lower(make) = lower(@make) and lower(model) = lower(@model) and model_code is not null and year is not null
            group by model_code
            """, new { make, model });
        return rows.Select(r => new CatalogGeneration(r.Code, r.Code, r.From ?? 0, r.To ?? 0, r.Seen, false)).ToList();
    }
}
