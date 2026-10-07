using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Mitsuke.Core;

namespace Mitsuke.Functions;

/// <summary>Asks for one watchlist to be collected. Ids only: the collector loads current state itself.</summary>
public sealed record CollectRequest(Guid WatchlistId);

/// <summary>
/// The 24/7 pipeline as three small functions joined by Storage queues:
///
///   ScheduleCollection (timer) --collect--> Collect (per watchlist) --alerts--> SendAlert (per match)
///
/// Each stage retries independently: a queue message that throws is redelivered (host.json maxDequeueCount),
/// then moved to "{queue}-poison" (the dead-letter queue) for inspection instead of being lost.
/// Collect is idempotent (upserts) and SendAlert is guarded by the alert log's claim, so redelivery is always safe.
/// </summary>
public sealed partial class PipelineFunctions(
    IWatchlistStore watchlists, Collector collector, AlertSender sender, ILogger<PipelineFunctions> logger)
{
    public const string CollectQueue = "collect";
    public const string AlertsQueue = "alerts";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Fans out one collect message per active watchlist. Schedule from app setting CollectSchedule.</summary>
    [Function(nameof(ScheduleCollection))]
    [QueueOutput(CollectQueue)]
    public async Task<string[]> ScheduleCollection([TimerTrigger("%CollectSchedule%")] TimerInfo timer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timer);
        if (timer.IsPastDue) LogPastDue(logger);

        var active = await watchlists.GetActiveAsync(cancellationToken);
        LogScheduled(logger, active.Count);
        return active.Select(w => JsonSerializer.Serialize(new CollectRequest(w.Id), Json)).ToArray();
    }

    [Function(nameof(Collect))]
    [QueueOutput(AlertsQueue)]
    public async Task<string[]> Collect([QueueTrigger(CollectQueue)] CollectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The watchlist may have been paused or deleted since it was scheduled.
        if (await watchlists.GetAsync(request.WatchlistId, cancellationToken) is not { IsActive: true } watchlist) return [];

        var result = await collector.CollectAsync(watchlist, cancellationToken);
        return result.Matches.Select(m => JsonSerializer.Serialize(m, Json)).ToArray();
    }

    [Function(nameof(SendAlert))]
    public async Task SendAlert([QueueTrigger(AlertsQueue)] AlertRequest request, CancellationToken cancellationToken)
    {
        // Throws on a failed send so the queue redelivers; AlreadySent and Skipped complete the message.
        await sender.SendAsync(request, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduled collection for {Count} active watchlists")]
    private static partial void LogScheduled(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Collection timer is running late (host was asleep or busy)")]
    private static partial void LogPastDue(ILogger logger);
}
