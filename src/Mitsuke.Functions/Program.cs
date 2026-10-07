using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mitsuke.Core;
using Mitsuke.Data;
using Mitsuke.Kensaya;
using Mitsuke.Notifications;
using Mitsuke.Pricing;
using Mitsuke.Sources.TheCarApi;
using OpenTelemetry;

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

// Traces, metrics and logs to Application Insights when it's configured (in Azure); nothing locally.
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

await builder.Build().RunAsync();
