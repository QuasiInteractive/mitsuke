using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Queues;
using Mitsuke.Core;

namespace Mitsuke.Api;

/// <summary>
/// Puts "refresh this listing's details" on the pipeline's <c>details</c> queue. Remembers what it asked for, so a
/// popular lot page sends one message, not one per view; the pipeline also skips listings that are already current.
/// </summary>
public sealed partial class DetailsRefreshSender(QueueClient queue, TimeProvider clock, ILogger<DetailsRefreshSender> logger) : IDetailsRefreshRequests
{
    private static readonly TimeSpan AskAgainAfter = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _asked = new();

    public async Task RequestAsync(Guid listingId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        if (_asked.TryGetValue(listingId, out var at) && now - at < AskAgainAfter) return;
        _asked[listingId] = now;
        if (_asked.Count > 10_000) _asked.Clear(); // a bounded memo, not a cache worth evicting carefully

        try
        {
            await queue.SendMessageAsync(JsonSerializer.Serialize(new { listingId }, Json), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The page renders with what it has; the next view after AskAgainAfter tries again.
            LogFailed(logger, ex, listingId);
        }
    }

    /// <summary>
    /// The API's two ways into the pipeline, both send-only: the <c>details</c> queue (refresh a lot's saved details) and
    /// the <c>collect</c> queue (search a watchlist now). In Azure: DETAILS_QUEUE_URI and COLLECT_QUEUE_URI with the
    /// managed identity. Locally: DETAILS_QUEUE_CONNECTION (Azurite) for both. Unset: lot pages show what they have, and
    /// new watchlists wait for the next scheduled run.
    /// </summary>
    public static IServiceCollection AddPipelineQueues(IServiceCollection services, IConfiguration configuration)
    {
        var options = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 }; // what the Functions triggers read
        QueueClient? Client(string uriSetting, string queueName)
        {
            if (Uri.TryCreate(configuration[uriSetting], UriKind.Absolute, out var uri))
                return new QueueClient(uri, new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = configuration["AZURE_CLIENT_ID"] }), options);
            return configuration["DETAILS_QUEUE_CONNECTION"] is { Length: > 0 } connection ? new QueueClient(connection, queueName, options) : null;
        }

        if (Client("DETAILS_QUEUE_URI", "details") is { } details)
            services.AddSingleton<IDetailsRefreshRequests>(sp => new DetailsRefreshSender(details, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<DetailsRefreshSender>>()));
        if (Client("COLLECT_QUEUE_URI", "collect") is { } collect)
            services.AddSingleton<ICollectRequests>(sp => new CollectRequestSender(collect, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<CollectRequestSender>>()));
        return services;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't queue a details refresh for listing {ListingId}")]
    private static partial void LogFailed(ILogger logger, Exception ex, Guid listingId);
}
