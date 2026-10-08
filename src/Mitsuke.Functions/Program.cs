using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mitsuke;
using Mitsuke.Core;
using Mitsuke.Data;
using Mitsuke.Kensaya;
using Mitsuke.Notifications;
using Mitsuke.Pricing;
using Mitsuke.Sources.TheCarApi;
using OpenTelemetry;
using OpenTelemetry.Trace;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Settings come from local.settings.json locally and app settings / Key Vault references in Azure.
builder.Services.AddMitsukeData(builder.Configuration["MITSUKE_DB"] ?? "");
builder.Services.AddMitsukePricing();
builder.Services.AddTheCarApiSource(builder.Configuration);
builder.Services.AddMitsukeNotifications(builder.Configuration);
builder.Services.AddKensayaSheetDecoding(builder.Configuration);
builder.Services.AddSingleton<Collector>();
builder.Services.AddSingleton<AlertSender>();
builder.Services.AddSingleton<DetailsRefresher>();
builder.Services.AddSingleton<CatalogSync>();
// MITSUKE_WEB_URL (e.g. https://mitsuke.vercel.app/) makes every alert link to its lot page.
if (Uri.TryCreate(builder.Configuration["MITSUKE_WEB_URL"], UriKind.Absolute, out var webUrl))
    builder.Services.AddSingleton(new AlertLinks(webUrl.AbsoluteUri.EndsWith('/') ? webUrl : new Uri(webUrl.AbsoluteUri + "/")));

// Traces, metrics and logs to Application Insights when it's configured (in Azure); nothing locally.
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .WithTracing(t => t.AddHttpClientInstrumentation()) // TheCarApi, Kensa-ya, Discord, push: as dependencies
        .UseAzureMonitorExporter();
    builder.Services.AddMitsukeTelemetryRules();
}

// The HTTP calls are dependencies now; their "Sending HTTP request" log lines would only repeat them.
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

await builder.Build().RunAsync();
