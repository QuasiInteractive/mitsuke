using Microsoft.Extensions.DependencyInjection;
using Mitsuke.Core;
using Npgsql;

namespace Mitsuke.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMitsukeData(this IServiceCollection services, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("MITSUKE_DB is not set. See .env.example.");

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<Migrator>();
        services.AddSingleton<IListingStore, PostgresListingStore>();
        services.AddSingleton<IWatchlistStore, PostgresWatchlistStore>();
        services.AddSingleton<IAlertLog, PostgresAlertLog>();
        services.AddSingleton<IListingDetailsStore, PostgresListingDetailsStore>();
        services.AddSingleton<IComparablesStore, PostgresComparablesStore>();
        services.AddSingleton<ISheetReportStore, PostgresSheetReportStore>();
        services.AddSingleton<IReadQueries, PostgresReadQueries>();
        services.AddSingleton<IBidRequestStore, PostgresBidRequestStore>();
        return services;
    }
}
