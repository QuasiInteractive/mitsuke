using Microsoft.Extensions.Logging;

namespace Mitsuke.Core;

public sealed record ScanResult(int Watchlists, int Seen, int New, int Matched, int Sent, int Failed, int AlreadyAlerted);

/// <summary>
/// The whole pipeline in one process (the CLI's <c>scan</c>): collect every active watchlist, then send each match.
/// Azure Functions runs the same two stages with a queue in between instead. Safe to run repeatedly.
/// </summary>
public sealed partial class Scanner(Collector collector, AlertSender sender, IWatchlistStore watchlists, ILogger<Scanner> logger)
{
    public async Task<ScanResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var active = await watchlists.GetActiveAsync(cancellationToken);
        int seen = 0, fresh = 0, matched = 0, sent = 0, failed = 0, already = 0;

        foreach (var watchlist in active)
        {
            var collected = await collector.CollectAsync(watchlist, cancellationToken);
            seen += collected.Seen;
            fresh += collected.New;
            matched += collected.Matches.Count;

            foreach (var request in collected.Matches)
            {
                try
                {
                    switch (await sender.SendAsync(request, cancellationToken))
                    {
                        case AlertOutcome.Sent: sent++; break;
                        case AlertOutcome.AlreadySent: already++; break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Already recorded as failed by the sender; one bad send must not stop the scan.
                    failed++;
                }
            }
        }

        var result = new ScanResult(active.Count, seen, fresh, matched, sent, failed, already);
        LogDone(logger, result.Watchlists, result.Seen, result.New, result.Matched, result.Sent, result.Failed, result.AlreadyAlerted);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Scan done: {Watchlists} watchlists, {Seen} listings ({New} new), {Matched} matched, {Sent} sent, {Failed} failed, {Already} already alerted")]
    private static partial void LogDone(ILogger logger, int watchlists, int seen, int @new, int matched, int sent, int failed, int already);
}
