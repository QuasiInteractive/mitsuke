using System.Net.Http.Json;
using Mitsuke.Core;

namespace Mitsuke.Cli;

/// <summary>Free test channel. The webhook URL is a secret (anyone holding it can post), so it comes from DISCORD_WEBHOOK_URL.</summary>
internal sealed class DiscordWebhookNotifier(HttpClient http, Uri webhook) : INotifier
{
    public string Channel => "discord";

    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        // Discord caps content at 2000 chars; alerts are far shorter, but never fail a send over it.
        var content = message.Length > 2000 ? message[..1997] + "..." : message;
        using var response = await http.PostAsJsonAsync(webhook, new { content }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Fallback when no webhook is configured: alerts go to the console.</summary>
internal sealed class ConsoleNotifier : INotifier
{
    public string Channel => "console";

    public Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        Console.WriteLine(message);
        Console.WriteLine(new string('-', 60));
        return Task.CompletedTask;
    }
}
