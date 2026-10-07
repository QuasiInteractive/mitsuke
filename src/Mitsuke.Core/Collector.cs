using Microsoft.Extensions.Logging;

namespace Mitsuke.Core;

/// <summary>A match to alert on. Small and id-only so it travels well as a queue message.</summary>
public sealed record AlertRequest(Guid WatchlistId, Guid ListingId);

public sealed record CollectResult(int Seen, int New, IReadOnlyList<AlertRequest> Matches);

/// <summary>
/// Stage 1 of the pipeline for one watchlist: search every source, store what's seen (dedupe + price history),
/// and return the matches. Sending is <see cref="AlertSender"/>'s job, so the two can run apart (via a queue).
/// </summary>
public sealed partial class Collector(
    IEnumerable<IListingSource> sources,
    IListingStore listings,
    ILandedCostEstimator landedCost,
    TimeProvider clock,
    ILogger<Collector> logger)
{
    public async Task<CollectResult> CollectAsync(Watchlist watchlist, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        int seen = 0, fresh = 0;
        var matches = new List<AlertRequest>();

        foreach (var source in sources)
        {
            await foreach (var listing in source.SearchAsync(watchlist.ToSourceQuery(), cancellationToken))
            {
                seen++;
                var stored = await listings.UpsertAsync(listing, cancellationToken);
                if (stored.IsNew) fresh++;

                // Local maths plus a cached exchange rate: cheap enough to run for every listing.
                var landed = listing.Price is { } price
                    ? await landedCost.EstimateAsync(price, watchlist.Destination, cancellationToken)
                    : null;

                var mismatches = WatchlistMatcher.Mismatches(watchlist, listing, clock.GetUtcNow(), landed);
                if (mismatches.Count > 0)
                {
                    LogRejected(logger, listing.Key, watchlist.Name, mismatches);
                    continue;
                }

                // Already-alerted matches are emitted too: the alert log's claim, not the collector, is what
                // guarantees once-only delivery, so a lost queue message can never mean a lost alert.
                matches.Add(new AlertRequest(watchlist.Id, stored.ListingId));
            }
        }

        LogCollected(logger, watchlist.Name, seen, fresh, matches.Count);
        return new CollectResult(seen, fresh, matches);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Key} rejected for '{Watchlist}': {Reasons}")]
    private static partial void LogRejected(ILogger logger, ListingKey key, string watchlist, IReadOnlyList<string> reasons);

    [LoggerMessage(Level = LogLevel.Information, Message = "Collected '{Watchlist}': {Seen} listings ({New} new), {Matched} matched")]
    private static partial void LogCollected(ILogger logger, string watchlist, int seen, int @new, int matched);
}
