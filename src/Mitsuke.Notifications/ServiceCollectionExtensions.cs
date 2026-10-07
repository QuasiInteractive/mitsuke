using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mitsuke.Core;

namespace Mitsuke.Notifications;

public static class ServiceCollectionExtensions
{
    /// <summary>Discord when DISCORD_WEBHOOK_URL is set, otherwise the console.</summary>
    public static IServiceCollection AddMitsukeNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddHttpClient("discord", http => http.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<INotifier>(sp =>
            Uri.TryCreate(configuration["DISCORD_WEBHOOK_URL"], UriKind.Absolute, out var hook)
                ? new DiscordWebhookNotifier(sp.GetRequiredService<IHttpClientFactory>().CreateClient("discord"), hook)
                : new ConsoleNotifier());
        return services;
    }
}
