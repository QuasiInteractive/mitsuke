using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mitsuke.Core;

namespace Mitsuke.Notifications;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Channels: Discord when DISCORD_WEBHOOK_URL is set, otherwise the console, for system watchlists; email over
    /// SMTP (SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASSWORD, SMTP_FROM) for people's own watchlists when configured.
    /// </summary>
    public static IServiceCollection AddMitsukeNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddHttpClient("discord", http => http.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<INotifier>(sp =>
            Uri.TryCreate(configuration["DISCORD_WEBHOOK_URL"], UriKind.Absolute, out var hook)
                ? new DiscordWebhookNotifier(sp.GetRequiredService<IHttpClientFactory>().CreateClient("discord"), hook)
                : new ConsoleNotifier());

        if (configuration["SMTP_HOST"] is { Length: > 0 } host)
        {
            var user = configuration["SMTP_USER"] is { Length: > 0 } u ? u : null;
            var from = configuration["SMTP_FROM"] is { Length: > 0 } f ? f : user ?? "alerts@mitsuke.local";
            services.AddSingleton<IEmailSender>(new SmtpEmailSender(new SmtpOptions
            {
                Host = host,
                Port = int.TryParse(configuration["SMTP_PORT"], out var port) ? port : 587,
                User = user,
                Password = configuration["SMTP_PASSWORD"]?.Replace(" ", "", StringComparison.Ordinal),
                FromAddress = from,
            }));
        }
        return services;
    }
}
