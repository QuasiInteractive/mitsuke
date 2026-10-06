// Mitsuke local runner.
//
//   dotnet run --project src/Mitsuke.Cli -- scan
//
// Runs one pass of the pipeline (collect -> normalise -> match -> notify) for a hard-coded watchlist.
// Watchlists move to Postgres next; the Azure Functions host replaces this runner later.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mitsuke.Cli;
using Mitsuke.Core;
using Mitsuke.Sources.TheCarApi;

if (args is not ["scan"])
{
    Console.Error.WriteLine("usage: dotnet run --project src/Mitsuke.Cli -- scan");
    return 2;
}

DotEnv.Load(".env");

var builder = Host.CreateApplicationBuilder();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);
builder.Logging.AddFilter("Polly", LogLevel.Warning); // retries and breaker trips still show
builder.Services.AddTheCarApiSource(builder.Configuration);
builder.Services.AddHttpClient("discord");
builder.Services.AddSingleton<INotifier>(sp =>
    Uri.TryCreate(builder.Configuration["DISCORD_WEBHOOK_URL"], UriKind.Absolute, out var hook)
        ? new DiscordWebhookNotifier(sp.GetRequiredService<IHttpClientFactory>().CreateClient("discord"), hook)
        : new ConsoleNotifier());

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Mitsuke.Scan");
var sources = host.Services.GetServices<IListingSource>().ToList();
var notifier = host.Services.GetRequiredService<INotifier>();

var watchlist = new Watchlist
{
    Id = Guid.Parse("7c3f2a10-0000-4000-8000-000000000032"),
    Name = "R32 GT-R",
    Make = "Nissan",
    Model = "Skyline",
    ModelCodes = new HashSet<string> { "BNR32" },
    YearFrom = 1989,
    YearTo = 1994,
    MaxMileageKm = 150_000,
    MinGrade = 3.5m,
    // ~A$45K landed works back to roughly ¥4.5M at auction; replaced by a real landed-cost limit later.
    MaxPrice = new Money(4_500_000m, "JPY"),
};

var seen = 0;
var matched = 0;
foreach (var source in sources)
{
    await foreach (var listing in source.SearchAsync(watchlist.ToSourceQuery()))
    {
        seen++;
        var mismatches = WatchlistMatcher.Mismatches(watchlist, listing, TimeProvider.System.GetUtcNow());
        if (mismatches.Count > 0)
        {
            Log.Rejected(log, listing.Key, mismatches);
            continue;
        }

        matched++;
        await notifier.SendAsync(AlertFormatter.Format(watchlist, listing));
    }
}

Log.ScanDone(log, seen, matched, watchlist.Name, notifier.Channel);
return 0;
