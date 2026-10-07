// Mitsuke local runner. Needs `docker compose up -d` and a filled-in .env.
//
//   dotnet run --project src/Mitsuke.Cli -- migrate   # create/upgrade the schema
//   dotnet run --project src/Mitsuke.Cli -- seed      # add the demo R32 GT-R watchlist
//   dotnet run --project src/Mitsuke.Cli -- scan      # one pass: collect -> store -> match -> alert once
//   dotnet run --project src/Mitsuke.Cli -- backfill  # one-off: load ended lots as deal-score comparables
//   dotnet run --project src/Mitsuke.Cli -- test-email you@example.com   # check the SMTP settings work
//
// The Azure Functions host replaces this runner later; the pipeline itself lives in Mitsuke.Core.Scanner.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mitsuke.Cli;
using Mitsuke.Core;
using Mitsuke.Data;
using Mitsuke.Kensaya;
using Mitsuke.Notifications;
using Mitsuke.Pricing;
using Mitsuke.Sources.TheCarApi;

var command = args switch
{
    [("migrate" or "seed" or "scan" or "backfill") and var c] => c,
    ["test-email", _] => "test-email",
    _ => null,
};
if (command is null)
{
    Console.Error.WriteLine("usage: dotnet run --project src/Mitsuke.Cli -- migrate|seed|scan|backfill|test-email <address>");
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
builder.Services.AddSingleton<Collector>();
builder.Services.AddSingleton<AlertSender>();
// MITSUKE_WEB_URL (e.g. https://mitsuke.vercel.app/) makes every alert link to its lot page.
if (Uri.TryCreate(builder.Configuration["MITSUKE_WEB_URL"], UriKind.Absolute, out var webUrl))
    builder.Services.AddSingleton(new AlertLinks(webUrl.AbsoluteUri.EndsWith('/') ? webUrl : new Uri(webUrl.AbsoluteUri + "/")));
builder.Services.AddSingleton<Scanner>();
builder.Services.AddMitsukeNotifications(builder.Configuration);
builder.Services.AddKensayaSheetDecoding(builder.Configuration);
if (command is "scan" or "backfill") builder.Services.AddTheCarApiSource(builder.Configuration); // only these need the API key

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

    case "test-email":
        if (services.GetService<IEmailSender>() is not { } email)
        {
            Console.Error.WriteLine("No email sender: set SMTP_HOST (and SMTP_USER / SMTP_PASSWORD for Gmail) in .env.");
            return 1;
        }
        await email.SendAsync(new EmailMessage(
            args[1],
            "Mitsuke test email",
            "If you can read this, Mitsuke's email alerts are set up correctly.",
            "<p style=\"font-family:Segoe UI,Arial,sans-serif\">If you can read this, <strong>Mitsuke</strong>'s email alerts are set up correctly. 見つけ</p>"));
        Console.WriteLine($"Sent a test email to {args[1]}.");
        break;

    case "backfill":
        // One-off: load ended lots for each watchlist so the deal score has comparables from day one.
        var listingStore = services.GetRequiredService<IListingStore>();
        foreach (var watchlist in await services.GetRequiredService<IWatchlistStore>().GetActiveAsync())
        foreach (var archive in services.GetServices<IArchiveSource>())
        {
            var (stored, added) = (0, 0);
            await foreach (var listing in archive.SearchArchiveAsync(watchlist.ToSourceQuery()))
            {
                stored++;
                if ((await listingStore.UpsertAsync(listing)).IsNew) added++;
            }
            Console.WriteLine($"'{watchlist.Name}' from {archive.Name} archive: {stored} lots, {added} new.");
        }
        break;
}

return 0;
