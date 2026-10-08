using System.Net.Http.Headers;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Mitsuke.Core;
using Polly;

namespace Mitsuke.Sources.TheCarApi;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTheCarApiSource(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TheCarApiOptions>()
            .Bind(configuration.GetSection(TheCarApiOptions.SectionName))
            .PostConfigure(o =>
            {
                // The brief names the env var THECARAPI_KEY; honour it without making people nest config keys.
                if (string.IsNullOrWhiteSpace(o.ApiKey)) o.ApiKey = configuration["THECARAPI_KEY"] ?? "";
            })
            .ValidateDataAnnotations()
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "THECARAPI_KEY is not set.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);

        services.AddHttpClient<TheCarApiSource>((sp, http) =>
            {
                var o = sp.GetRequiredService<IOptions<TheCarApiOptions>>().Value;
                http.BaseAddress = o.BaseAddress;
                http.DefaultRequestHeaders.Add("X-API-Key", o.ApiKey);
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                http.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns timeouts
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            .AddResilienceHandler("thecarapi", (pipeline, context) =>
            {
                var o = context.ServiceProvider.GetRequiredService<IOptions<TheCarApiOptions>>().Value;

                // Outermost to innermost: total budget -> retry -> circuit breaker -> rate limit -> per-attempt timeout.
                // The rate limiter sits inside retry so every attempt, including retries, counts against our ceiling.
                pipeline
                    .AddTimeout(TimeSpan.FromSeconds(90))
                    .AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = TimeSpan.FromSeconds(2),
                        // Honours Retry-After on 429 by default.
                    })
                    .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = 0.5,
                        MinimumThroughput = 4,
                        SamplingDuration = TimeSpan.FromMinutes(2),
                        BreakDuration = TimeSpan.FromMinutes(5),
                    })
                    .AddRateLimiter(new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = o.RequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 100, // wait for a slot rather than fail
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    }))
                    .AddTimeout(TimeSpan.FromSeconds(20));
            });

        services.AddTransient<IListingSource>(sp => sp.GetRequiredService<TheCarApiSource>());
        services.AddTransient<IListingDetailsSource>(sp => sp.GetRequiredService<TheCarApiSource>());
        services.AddTransient<IArchiveSource>(sp => sp.GetRequiredService<TheCarApiSource>());
        services.AddTransient<ICatalogSource>(sp => new TheCarApiCatalog(sp.GetRequiredService<TheCarApiSource>()));
        return services;
    }
}
