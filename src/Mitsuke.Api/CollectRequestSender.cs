using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Storage.Queues;
using Mitsuke.Core;

namespace Mitsuke.Api;

/// <summary>
/// "Search this watchlist now": puts a message on the pipeline's <c>collect</c> queue, the same one the 10-minute timer
/// fills, so a new or changed watchlist shows its matches within seconds through the same retries and rate limits.
/// At most one request per watchlist per minute, however often someone presses save.
/// </summary>
public sealed partial class CollectRequestSender(QueueClient queue, TimeProvider clock, ILogger<CollectRequestSender> logger) : ICollectRequests
{
    private static readonly TimeSpan AtMostEvery = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _asked = new();

    public async Task RequestAsync(Guid watchlistId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        if (_asked.TryGetValue(watchlistId, out var at) && now - at < AtMostEvery) return;
        _asked[watchlistId] = now;
        if (_asked.Count > 10_000) _asked.Clear();

        try
        {
            // Same shape as the timer's CollectRequest; ScheduledAt lets the pipeline drop it if it's ever redelivered late.
            await queue.SendMessageAsync(JsonSerializer.Serialize(new { watchlistId, scheduledAt = now }, Json), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not fatal: the next scheduled run (at most ten minutes away) searches it anyway.
            LogFailed(logger, ex, watchlistId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't queue an immediate search for watchlist {WatchlistId}; the next scheduled run will do it")]
    private static partial void LogFailed(ILogger logger, Exception ex, Guid watchlistId);
}
