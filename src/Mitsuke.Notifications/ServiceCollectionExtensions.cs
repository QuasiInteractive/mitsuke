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

        // Phone/desktop push when VAPID keys are configured (VAPID_PUBLIC_KEY, VAPID_PRIVATE_KEY, VAPID_SUBJECT).
        // An unresolved Key Vault reference (secret not set yet) arrives as its literal text: leave push off rather
        // than fail every alert, email included.
        if (configuration["VAPID_PUBLIC_KEY"] is { Length: > 0 } publicKey
            && configuration["VAPID_PRIVATE_KEY"] is { Length: > 0 } privateKey
            && !privateKey.StartsWith("@Microsoft.KeyVault", StringComparison.Ordinal))
        {
            var vapid = new VapidOptions
            {
                PublicKey = publicKey,
                PrivateKey = privateKey,
                Subject = configuration["VAPID_SUBJECT"] is { Length: > 0 } s ? s : "mailto:mitsuke.alerts@gmail.com",
            };
            services.AddHttpClient("webpush", http => http.Timeout = TimeSpan.FromSeconds(15));
            services.AddSingleton<IPushSender>(sp => new WebPushSender(sp.GetRequiredService<IHttpClientFactory>().CreateClient("webpush"), vapid));
        }
        return services;
    }
}
