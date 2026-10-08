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
    /// DETAILS_QUEUE_URI (Azure, managed identity) or DETAILS_QUEUE_CONNECTION (local Azurite). Neither: no background
    /// refresh, and lot pages simply show the details they have.
    /// </summary>
    public static IServiceCollection AddDetailsRefresh(IServiceCollection services, IConfiguration configuration)
    {
        var options = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 }; // what the Functions trigger reads
        QueueClient? client = null;
        if (Uri.TryCreate(configuration["DETAILS_QUEUE_URI"], UriKind.Absolute, out var uri))
            client = new QueueClient(uri, new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = configuration["AZURE_CLIENT_ID"] }), options);
        else if (configuration["DETAILS_QUEUE_CONNECTION"] is { Length: > 0 } connection)
            client = new QueueClient(connection, "details", options);

        if (client is not null)
            services.AddSingleton<IDetailsRefreshRequests>(sp => new DetailsRefreshSender(client, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<DetailsRefreshSender>>()));
        return services;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't queue a details refresh for listing {ListingId}")]
    private static partial void LogFailed(ILogger logger, Exception ex, Guid listingId);
}
