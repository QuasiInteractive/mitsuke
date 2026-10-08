using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Mitsuke.Core;

namespace Mitsuke.Functions;

/// <summary>
/// Asks for one watchlist to be collected. Ids only: the collector loads current state itself.
/// <paramref name="ScheduledAt"/> lets a late redelivery see that a newer run has already replaced it.
/// </summary>
public sealed record CollectRequest(Guid WatchlistId, DateTimeOffset? ScheduledAt = null);

/// <summary>
/// The 24/7 pipeline as three small functions joined by Storage queues:
///
///   ScheduleCollection (timer) --collect--> Collect (per watchlist) --alerts--> SendAlert (per match)
///
/// Each stage retries independently: a queue message that throws is redelivered up to host.json's
/// maxDequeueCount. Collect is idempotent (upserts) and SendAlert is guarded by the alert log's claim, so
/// redelivery is always safe.
///
/// The two queues fail differently on purpose. A collect message is only "look at the market now", and the next
/// timer run asks again, so a stale one is dropped and the last failed attempt gives up (logged as an error)
/// instead of filling collect-poison with work that's already been redone. An alert message is a promise to tell
/// someone about a car, so it keeps the dead-letter queue: after the last attempt it moves to alerts-poison.
/// </summary>
public sealed partial class PipelineFunctions(
    IWatchlistStore watchlists, Collector collector, AlertSender sender, TimeProvider clock, ILogger<PipelineFunctions> logger)
{
    public const string CollectQueue = "collect";
    public const string AlertsQueue = "alerts";

    /// <summary>Mirrors host.json extensions.queues.maxDequeueCount (a test keeps the two in step).</summary>
    public const int MaxDequeueCount = 5;

    /// <summary>
    /// A collect message older than this has been overtaken by a newer run (the schedule is every 10 minutes).
    /// </summary>
    public static readonly TimeSpan CollectStaleAfter = TimeSpan.FromMinutes(9);

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
        var now = clock.GetUtcNow();
        return active.Select(w => JsonSerializer.Serialize(new CollectRequest(w.Id, now), Json)).ToArray();
    }

    [Function(nameof(Collect))]
    [QueueOutput(AlertsQueue)]
    public Task<string[]> Collect([QueueTrigger(CollectQueue)] CollectRequest request, FunctionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var attempt = context.BindingContext.BindingData.TryGetValue("DequeueCount", out var n) && long.TryParse(n?.ToString(), out var count) ? count : 1;
        return CollectAsync(request, attempt, cancellationToken);
    }

    internal async Task<string[]> CollectAsync(CollectRequest request, long attempt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ScheduledAt is { } scheduledAt && clock.GetUtcNow() - scheduledAt > CollectStaleAfter)
        {
            LogStale(logger, request.WatchlistId, scheduledAt);
            return [];
        }

        try
        {
            // The watchlist may have been paused or deleted since it was scheduled.
            if (await watchlists.GetAsync(request.WatchlistId, cancellationToken) is not { IsActive: true } watchlist) return [];

            var result = await collector.CollectAsync(watchlist, cancellationToken);
            return result.Matches.Select(m => JsonSerializer.Serialize(m, Json)).ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException && attempt >= MaxDequeueCount)
        {
            // Completing the message (rather than letting it go to collect-poison) is safe: the next timer run asks again.
            LogGaveUp(logger, ex, request.WatchlistId, attempt);
            return [];
        }
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Dropped stale collect for {WatchlistId} scheduled at {ScheduledAt}: a newer run replaces it")]
    private static partial void LogStale(ILogger logger, Guid watchlistId, DateTimeOffset scheduledAt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Gave up collecting {WatchlistId} after {Attempts} attempts; the next scheduled run retries it")]
    private static partial void LogGaveUp(ILogger logger, Exception exception, Guid watchlistId, long attempts);
}
