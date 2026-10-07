// Mitsuke local runner. Needs `docker compose up -d` and a filled-in .env.
//
//   dotnet run --project src/Mitsuke.Cli -- migrate   # create/upgrade the schema
//   dotnet run --project src/Mitsuke.Cli -- seed      # add the demo R32 GT-R watchlist
//   dotnet run --project src/Mitsuke.Cli -- scan      # one pass: collect -> store -> match -> alert once
//
// The Azure Functions host replaces this runner later; the pipeline itself lives in Mitsuke.Core.Scanner.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mitsuke.Cli;
using Mitsuke.Core;
using Mitsuke.Data;
using Mitsuke.Pricing;
using Mitsuke.Sources.TheCarApi;

if (args is not [("migrate" or "seed" or "scan") and var command])
{
    Console.Error.WriteLine("usage: dotnet run --project src/Mitsuke.Cli -- migrate|seed|scan");
    return 2;
}

DotEnv.Load(".env");

var builder = Host.CreateApplicationBuilder();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);
builder.Logging.AddFilter("Polly", LogLevel.Warning); // retries and breaker trips still show

builder.Services.AddMitsukeData(builder.Configuration["MITSUKE_DB"] ?? "");
builder.Services.AddMitsukePricing();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Scanner>();
builder.Services.AddHttpClient("discord");
builder.Services.AddSingleton<INotifier>(sp =>
    Uri.TryCreate(builder.Configuration["DISCORD_WEBHOOK_URL"], UriKind.Absolute, out var hook)
        ? new DiscordWebhookNotifier(sp.GetRequiredService<IHttpClientFactory>().CreateClient("discord"), hook)
        : new ConsoleNotifier());
if (command == "scan") builder.Services.AddTheCarApiSource(builder.Configuration); // only scan needs the API key

using var host = builder.Build();
var services = host.Services;

switch (command)
{
    case "migrate":
        var applied = await services.GetRequiredService<Migrator>().MigrateAsync();
        Console.WriteLine(applied == 0 ? "Schema is up to date." : $"Applied {applied} migration(s).");
        break;

    case "seed":
        var store = services.GetRequiredService<IWatchlistStore>();
        if ((await store.GetActiveAsync()).Any(w => w.Id == DemoWatchlist.R32Gtr.Id))
        {
            Console.WriteLine("Demo watchlist already exists.");
            break;
        }
        await store.AddAsync(DemoWatchlist.R32Gtr);
        Console.WriteLine($"Added watchlist '{DemoWatchlist.R32Gtr.Name}'.");
        break;

    case "scan":
        await services.GetRequiredService<Scanner>().RunAsync();
        break;
}

return 0;
