using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Metrics;

namespace Mitsuke;

/// <summary>
/// Telemetry rules shared by the API and the pipeline (compiled into both). Two jobs: keep ingestion well under
/// the Log Analytics daily cap by dropping metrics nobody charts, and keep secrets out of dependency telemetry.
/// </summary>
internal static class MitsukeTelemetry
{
    /// <summary>
    /// Gauges emitted every minute per connection pool or GC generation. They were ~70% of ingestion; request
    /// durations (what the dashboard uses) and the requests/dependencies/exceptions tables are kept.
    /// </summary>
    private static readonly string[] DroppedMetrics =
    [
        "http.client.open_connections", "http.client.active_requests", "http.client.connection.duration",
        "http.server.active_requests", "dotnet.*", "process.*", "kestrel.*", "aspnetcore.*",
    ];

    /// <summary>Web Push endpoints carry the device's subscription token in the path; keep only the host.</summary>
    private static readonly string[] PushHosts = ["fcm.googleapis.com", "web.push.apple.com", "updates.push.services.mozilla.com", "notify.windows.com"];

    public static IServiceCollection AddMitsukeTelemetryRules(this IServiceCollection services)
    {
        services.ConfigureOpenTelemetryMeterProvider(metrics =>
        {
            foreach (var name in DroppedMetrics) metrics.AddView(name, MetricStreamConfiguration.Drop);
        });
        services.Configure<HttpClientTraceInstrumentationOptions>(o => o.EnrichWithHttpRequestMessage = RedactPushEndpoint);
        return services;
    }

    internal static void RedactPushEndpoint(Activity activity, HttpRequestMessage request)
    {
        if (request.RequestUri is { } uri && IsPushHost(uri.Host))
            activity.SetTag("url.full", $"{uri.Scheme}://{uri.Host}/[device]");
    }

    internal static bool IsPushHost(string host) =>
        PushHosts.Any(p => host.Equals(p, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + p, StringComparison.OrdinalIgnoreCase));
}
