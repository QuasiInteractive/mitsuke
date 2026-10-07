using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Mitsuke.Core;

namespace Mitsuke.Notifications;

public sealed class SmtpOptions
{
    public required string Host { get; init; }
    public int Port { get; init; } = 587;
    public string? User { get; init; }
    public string? Password { get; init; }
    public required string FromAddress { get; init; }
    public string FromName { get; init; } = "Mitsuke";
}

/// <summary>
/// Sends email over SMTP: Gmail (smtp.gmail.com:587, STARTTLS, an app password) in production, the local Mailpit
/// sink in development. A failure throws, so the alert is marked failed and the queue retries it.
/// </summary>
public sealed class SmtpEmailSender(SmtpOptions options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.Text, HtmlBody = message.Html }.ToMessageBody();

        using var smtp = new SmtpClient { Timeout = 30_000 };
        // Real servers get TLS (required for Gmail); the local sink has none.
        var security = options.Port == 465 ? SecureSocketOptions.SslOnConnect
            : options.User is null ? SecureSocketOptions.None : SecureSocketOptions.StartTls;
        await smtp.ConnectAsync(options.Host, options.Port, security, cancellationToken);
        if (options.User is not null) await smtp.AuthenticateAsync(options.User, options.Password ?? "", cancellationToken);
        await smtp.SendAsync(mime, cancellationToken);
        await smtp.DisconnectAsync(quit: true, cancellationToken);
    }
}
