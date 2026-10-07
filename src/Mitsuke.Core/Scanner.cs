using Microsoft.Extensions.Logging;

namespace Mitsuke.Core;

public sealed record ScanResult(int Watchlists, int Seen, int New, int Matched, int Sent, int Failed, int AlreadyAlerted);

/// <summary>
/// One pass of the pipeline: collect -> store (dedupe + price history) -> match -> alert once.
/// Safe to run repeatedly: a listing is alerted once per watchlist and channel, and a failed send is retried next pass.
/// </summary>
public sealed partial class Scanner(
    IEnumerable<IListingSource> sources,
    IListingStore listings,
    IWatchlistStore watchlists,
    IAlertLog alerts,
    INotifier notifier,
    TimeProvider clock,
    ILogger<Scanner> logger)
{
    public async Task<ScanResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var active = await watchlists.GetActiveAsync(cancellationToken);
        int seen = 0, fresh = 0, matched = 0, sent = 0, failed = 0, already = 0;

        // One search per watchlist per source. Fine at this scale; group identical queries before it isn't.
        foreach (var watchlist in active)
        {
            foreach (var source in sources)
            {
                await foreach (var listing in source.SearchAsync(watchlist.ToSourceQuery(), cancellationToken))
                {
                    seen++;
                    var stored = await listings.UpsertAsync(listing, cancellationToken);
                    if (stored.IsNew) fresh++;

                    var mismatches = WatchlistMatcher.Mismatches(watchlist, listing, clock.GetUtcNow());
                    if (mismatches.Count > 0)
                    {
                        LogRejected(logger, listing.Key, watchlist.Name, mismatches);
                        continue;
                    }

                    matched++;
                    if (await alerts.TryClaimAsync(watchlist.Id, stored.ListingId, notifier.Channel, cancellationToken) is not { } alertId)
                    {
                        already++;
                        continue;
                    }

                    try
                    {
                        await notifier.SendAsync(AlertFormatter.Format(watchlist, listing), cancellationToken);
                        await alerts.MarkSentAsync(alertId, cancellationToken);
                        sent++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // One bad send must not stop the scan; the claim is released as 'failed' and retried next pass.
                        await alerts.MarkFailedAsync(alertId, ex.Message, cancellationToken);
                        LogSendFailed(logger, ex, listing.Key, notifier.Channel);
                        failed++;
                    }
                }
            }
        }

        var result = new ScanResult(active.Count, seen, fresh, matched, sent, failed, already);
        LogDone(logger, result.Watchlists, result.Seen, result.New, result.Matched, result.Sent, result.Failed, result.AlreadyAlerted);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Key} rejected for '{Watchlist}': {Reasons}")]
    private static partial void LogRejected(ILogger logger, ListingKey key, string watchlist, IReadOnlyList<string> reasons);

    [LoggerMessage(Level = LogLevel.Error, Message = "Alert for {Key} via {Channel} failed; will retry next scan")]
    private static partial void LogSendFailed(ILogger logger, Exception ex, ListingKey key, string channel);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Scan done: {Watchlists} watchlists, {Seen} listings ({New} new), {Matched} matched, {Sent} sent, {Failed} failed, {Already} already alerted")]
    private static partial void LogDone(ILogger logger, int watchlists, int seen, int @new, int matched, int sent, int failed, int already);
}
