using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Mitsuke.Core;
using Polly;

namespace Mitsuke.Kensaya;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Sheet decoding through Kensa-ya when KENSAYA_BASE_URL and KENSAYA_PARTNER_KEY are set; otherwise no decoder
    /// is registered and alerts simply go out without a decoded sheet.
    /// </summary>
    public static IServiceCollection AddKensayaSheetDecoding(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = new KensayaOptions
        {
            BaseAddress = Uri.TryCreate(configuration["KENSAYA_BASE_URL"], UriKind.Absolute, out var b) ? b : null,
            PartnerKey = configuration["KENSAYA_PARTNER_KEY"] ?? "",
        };
        if (!options.IsConfigured) return services;

        services.AddSingleton(Options.Create(options));
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<ISheetDecoder, KensayaSheetDecoder>(http =>
            {
                http.BaseAddress = options.BaseAddress;
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.PartnerKey);
                http.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns timeouts
            })
            .AddResilienceHandler("kensaya", pipeline =>
            {
                // Reading a sheet is a 20–60 s AI call, so attempts get two minutes and retries are few.
                // Retry honours Kensa-ya's Retry-After on 503 (AI provider busy). The breaker stops every alert
                // from waiting on a Kensa-ya outage: it fails fast and alerts go out without the sheet.
                pipeline
                    .AddTimeout(TimeSpan.FromMinutes(5))
                    .AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 2,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                        Delay = TimeSpan.FromSeconds(5),
                    })
                    .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = 0.5,
                        MinimumThroughput = 3,
                        SamplingDuration = TimeSpan.FromMinutes(5),
                        BreakDuration = TimeSpan.FromMinutes(10),
                    })
                    .AddTimeout(TimeSpan.FromMinutes(2));
            });
        return services;
    }
}
