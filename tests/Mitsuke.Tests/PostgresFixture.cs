using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Mitsuke.Data;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Mitsuke.Tests;

/// <summary>One throwaway Postgres container for all database tests, migrated once. Needs Docker.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public NpgsqlDataSource Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Db = NpgsqlDataSource.Create(_container.GetConnectionString());
        await new Migrator(Db, NullLogger<Migrator>.Instance).MigrateAsync();
    }

    /// <summary>Each test starts from empty tables.</summary>
    public async Task ResetAsync()
    {
        await using var conn = await Db.OpenConnectionAsync();
        await conn.ExecuteAsync("truncate alerts, sheet_reports, listing_details, price_observations, listings, vehicles, watchlists restart identity cascade");
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresTests : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
