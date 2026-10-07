using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Mitsuke.Data;

/// <summary>
/// Applies embedded <c>Migrations/NNNN_name.sql</c> scripts in order, each in its own transaction,
/// recording them in <c>schema_migrations</c>. Forward-only: a shipped script is never edited, only followed by a new one.
/// </summary>
public sealed partial class Migrator(NpgsqlDataSource db, ILogger<Migrator> logger)
{
    // Arbitrary constant: an advisory lock so two instances starting together can't migrate at once.
    private const long LockKey = 0x4D_49_54_53_55_4B_45; // "MITSUKE"

    public async Task<int> MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync("select pg_advisory_lock(@LockKey)", new { LockKey });
        try
        {
            // On Supabase, tables in "public" are reachable through its Data API with the publishable key the web app
            // ships. Production therefore sets "Search Path=mitsuke": the tables live in a private schema that API never
            // exposes, created here and closed to everyone but its owner.
            var schema = new NpgsqlConnectionStringBuilder(conn.ConnectionString).SearchPath?.Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(schema) && schema != "public")
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(schema, "^[a-z_][a-z0-9_]*$"))
                    throw new InvalidOperationException($"Unexpected schema name '{schema}' in Search Path.");
                await conn.ExecuteAsync($"create schema if not exists {schema}; revoke all on schema {schema} from public");
            }

            await conn.ExecuteAsync("""
                create table if not exists schema_migrations (
                    id         text primary key,
                    applied_at timestamptz not null default now()
                )
                """);

            var applied = (await conn.QueryAsync<string>("select id from schema_migrations")).ToHashSet();
            var count = 0;
            foreach (var (id, sql) in Scripts())
            {
                if (applied.Contains(id)) continue;

                await using var tx = await conn.BeginTransactionAsync(cancellationToken);
                await conn.ExecuteAsync(sql, transaction: tx);
                await conn.ExecuteAsync("insert into schema_migrations (id) values (@id)", new { id }, tx);
                await tx.CommitAsync(cancellationToken);

                LogApplied(logger, id);
                count++;
            }
            return count;
        }
        finally
        {
            await conn.ExecuteAsync("select pg_advisory_unlock(@LockKey)", new { LockKey });
        }
    }

    internal static IEnumerable<(string Id, string Sql)> Scripts()
    {
        var assembly = typeof(Migrator).Assembly;
        const string prefix = "Mitsuke.Data.Migrations.";
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return (name[prefix.Length..^".sql".Length], reader.ReadToEnd());
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied migration {Id}")]
    private static partial void LogApplied(ILogger logger, string id);
}
