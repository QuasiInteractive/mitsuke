using Microsoft.Extensions.DependencyInjection;
using Mitsuke.Core;
using Npgsql;

namespace Mitsuke.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>Connections per process when the connection string doesn't say: a few is plenty for this workload.</summary>
    public const int DefaultMaxPoolSize = 4;

    /// <summary>
    /// Seconds an idle pooled connection is kept before it's closed (Npgsql's default is 300). Every idle connection
    /// holds one of the pooler's 15 sessions, so they're handed back quickly; reopening one costs a few milliseconds.
    /// </summary>
    public const int DefaultIdleLifetimeSeconds = 60;

    /// <summary>
    /// Production goes through Supabase's session pooler, which allows 15 sessions in total across the API and every
    /// pipeline instance; Npgsql's default of 100 per process let one busy page exhaust it (8 Oct 2026). An explicit
    /// "Maximum Pool Size" in the connection string still wins.
    /// </summary>
    internal static string WithPoolCap(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!connectionString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("MaxPoolSize", StringComparison.OrdinalIgnoreCase))
            builder.MaxPoolSize = DefaultMaxPoolSize;
        if (!connectionString.Contains("Idle Lifetime", StringComparison.OrdinalIgnoreCase))
        {
            builder.ConnectionIdleLifetime = DefaultIdleLifetimeSeconds;
            builder.ConnectionPruningInterval = 10;
        }
        return builder.ConnectionString;
    }

    public static IServiceCollection AddMitsukeData(this IServiceCollection services, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("MITSUKE_DB is not set. See .env.example.");

        services.AddSingleton(_ => NpgsqlDataSource.Create(WithPoolCap(connectionString)));
        services.AddSingleton<Migrator>();
        services.AddSingleton<IListingStore, PostgresListingStore>();
        services.AddSingleton<IWatchlistStore, PostgresWatchlistStore>();
        services.AddSingleton<IAlertLog, PostgresAlertLog>();
        services.AddSingleton<IListingDetailsStore, PostgresListingDetailsStore>();
        services.AddSingleton<IComparablesStore, PostgresComparablesStore>();
        services.AddSingleton<ISheetReportStore, PostgresSheetReportStore>();
        services.AddSingleton<IReadQueries, PostgresReadQueries>();
        services.AddSingleton<IBidRequestStore, PostgresBidRequestStore>();
        services.AddSingleton<IUserStore, PostgresUserStore>();
        services.AddSingleton<IUserWatchlistStore, PostgresUserWatchlistStore>();
        services.AddSingleton<IFeedbackStore, PostgresFeedbackStore>();
        services.AddSingleton<IPushSubscriptionStore, PostgresPushSubscriptionStore>();
        services.AddSingleton<ICatalogStore, PostgresCatalogStore>();
        return services;
    }
}
