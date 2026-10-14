using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
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
    IWatchlistStore watchlists, Collector collector, AlertSender sender, TimeProvider clock, ILogger<PipelineFunctions> logger,
    IEmailSender? email = null, IConfiguration? config = null)
{
    /// <summary>How often a rejected-key email goes out at most (per instance): enough to notice, not a flood.</summary>
    public static readonly TimeSpan KeyAlertEvery = TimeSpan.FromHours(6);
    private static DateTimeOffset _lastKeyAlert = DateTimeOffset.MinValue;

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
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // The data provider rejected the API key (13 Oct 2026: revoked, unnoticed for a day, ~200 rejected calls an
            // hour from retries). Retrying can't fix a key, and hammering a provider with a dead key looks like abuse:
            // complete the message, log it as critical and tell a human.
            LogKeyRejected(logger, (int)ex.StatusCode!.Value, request.WatchlistId);
            await AlertKeyRejectedAsync((int)ex.StatusCode!.Value, cancellationToken);
            return [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException && attempt >= MaxDequeueCount)
        {
            // Completing the message (rather than letting it go to collect-poison) is safe: the next timer run asks again.
            LogGaveUp(logger, ex, request.WatchlistId, attempt);
            return [];
        }
    }

    private async Task AlertKeyRejectedAsync(int status, CancellationToken cancellationToken)
    {
        var to = config?["OPS_ALERTS_TO"] is { Length: > 0 } ops ? ops : config?["BID_REQUESTS_TO"];
        var now = clock.GetUtcNow();
        if (email is null || string.IsNullOrEmpty(to) || now - _lastKeyAlert < KeyAlertEvery) return;
        _lastKeyAlert = now;
        var text = $"TheCarApi answered {status} to Mitsuke's searches at {now:yyyy-MM-dd HH:mm} UTC: the API key has been rejected " +
            "(revoked, expired or over its plan). No new cars are coming in. Check the account, then put a working key in Key Vault " +
            "(secret thecarapi-key) and restart the pipeline. Searches are not retried while the key is rejected.";
        try
        {
            await email.SendAsync(new EmailMessage(to, "Mitsuke: the data provider rejected the API key", text,
                $"<p style=\"font-family:Segoe UI,Arial,sans-serif\">{System.Net.WebUtility.HtmlEncode(text)}</p>"), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogKeyAlertFailed(logger, ex);
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

    [LoggerMessage(Level = LogLevel.Critical, Message = "Data provider rejected the API key ({Status}) collecting {WatchlistId}; not retrying")]
    private static partial void LogKeyRejected(ILogger logger, int status, Guid watchlistId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't email the rejected-key alert")]
    private static partial void LogKeyAlertFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Gave up collecting {WatchlistId} after {Attempts} attempts; the next scheduled run retries it")]
    private static partial void LogGaveUp(ILogger logger, Exception exception, Guid watchlistId, long attempts);
}
