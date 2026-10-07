using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Mitsuke.Core;
using CorePushMessage = Mitsuke.Core.PushMessage;
using CorePushSubscription = Mitsuke.Core.PushSubscription;

namespace Mitsuke.Notifications;

public sealed class VapidOptions
{
    /// <summary>VAPID_PUBLIC_KEY: also given to browsers when they subscribe.</summary>
    public required string PublicKey { get; init; }

    /// <summary>VAPID_PRIVATE_KEY: a secret (Key Vault in production).</summary>
    public required string PrivateKey { get; init; }

    /// <summary>VAPID_SUBJECT: how push services can contact the sender, e.g. mailto:mitsuke.alerts@gmail.com.</summary>
    public required string Subject { get; init; }
}

/// <summary>
/// Web Push (RFC 8030) with VAPID: the payload is encrypted for the one device, so the push service (Google, Apple,
/// Mozilla) only carries it. A 404/410 means the device unsubscribed, which the caller turns into a delete.
/// </summary>
public sealed class WebPushSender(HttpClient http, VapidOptions vapid) : IPushSender
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PushServiceClient _client = new(http)
    {
        DefaultAuthentication = new VapidAuthentication(vapid.PublicKey, vapid.PrivateKey) { Subject = vapid.Subject },
    };

    public async Task<PushResult> SendAsync(CorePushSubscription subscription, CorePushMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(message);

        var target = new Lib.Net.Http.WebPush.PushSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);

        var payload = JsonSerializer.Serialize(new { title = message.Title, body = message.Body, url = message.Url?.ToString(), tag = message.Tag }, Json);
        var push = new Lib.Net.Http.WebPush.PushMessage(payload)
        {
            Urgency = PushMessageUrgency.High,
            TimeToLive = (int)TimeSpan.FromDays(2).TotalSeconds, // auctions move fast; an alert older than this is stale
        };

        try
        {
            await _client.RequestPushMessageDeliveryAsync(target, push, cancellationToken);
            return PushResult.Delivered;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushResult.Gone;
        }
    }
}
