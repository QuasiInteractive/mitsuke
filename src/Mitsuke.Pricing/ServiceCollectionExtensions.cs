using Kensaya.Worker.Core.Rules;
using Microsoft.Extensions.DependencyInjection;
using Mitsuke.Core;

namespace Mitsuke.Pricing;

public static class ServiceCollectionExtensions
{
    /// <summary>Landed-cost estimates with live ECB exchange rates (cached 6h, falling back to the rules file's rate).</summary>
    public static IServiceCollection AddMitsukePricing(this IServiceCollection services)
    {
        services.AddSingleton(_ => RulesCatalog.Load());
        // LiveFx caches rates in memory, so it must be one shared instance. A singleton keeps its HttpClient
        // for life, so the handler recycles pooled connections itself (picks up DNS changes) instead.
        services.AddHttpClient("fx").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        });
        services.AddSingleton<IRulesSource>(sp => ActivatorUtilities.CreateInstance<LiveFx>(
            sp, sp.GetRequiredService<IHttpClientFactory>().CreateClient("fx")));
        services.AddSingleton<ILandedCostEstimator, KensayaLandedCostEstimator>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
