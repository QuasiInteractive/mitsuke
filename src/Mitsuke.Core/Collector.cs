using Microsoft.Extensions.Logging;

namespace Mitsuke.Core;

/// <summary>Asks the pipeline to search a watchlist now instead of at the next scheduled run (after a create or edit).</summary>
public interface ICollectRequests
{
    Task RequestAsync(Guid watchlistId, CancellationToken cancellationToken = default);
}

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
        var rejected = new Dictionary<string, int>(StringComparer.Ordinal);

        // Store everything seen (dedupe + price history happen per listing), then match one copy per car: the feed
        // sometimes lists the same lot twice (ListingIdentity), and two copies must never mean two alerts.
        var stored = new List<(Listing Listing, Guid Id)>();
        foreach (var source in sources)
        {
            await foreach (var listing in source.SearchAsync(watchlist.ToSourceQuery(), cancellationToken))
            {
                seen++;
                var upsert = await listings.UpsertAsync(listing, cancellationToken);
                if (upsert.IsNew) fresh++;
                stored.Add((listing, upsert.ListingId));
            }
        }

        var cars = ListingIdentity.OnePerCar(stored, s => s.Listing).ToList();
        if (stored.Count > cars.Count) rejected["listed twice (kept one copy)"] = stored.Count - cars.Count;

        foreach (var (listing, listingId) in cars)
        {
            {
                // Local maths plus a cached exchange rate: cheap enough to run for every listing.
                var landed = listing.Price is { } price
                    ? await landedCost.EstimateAsync(price, watchlist.Destination, cancellationToken)
                    : null;

                var mismatches = WatchlistMatcher.Mismatches(watchlist, listing, clock.GetUtcNow(), landed);
                if (mismatches.Count > 0)
                {
                    LogRejected(logger, listing.Key, watchlist.Name, mismatches);
                    foreach (var reason in mismatches) rejected[reason] = rejected.GetValueOrDefault(reason) + 1;
                    continue;
                }

                // Already-alerted matches are emitted too: the alert log's claim, not the collector, is what
                // guarantees once-only delivery, so a lost queue message can never mean a lost alert.
                matches.Add(new AlertRequest(watchlist.Id, listingId));
            }
        }

        var rejections = SummariseRejections(rejected);
        LogCollected(logger, watchlist.Name, seen, fresh, matches.Count, rejections);
        return new CollectResult(seen, fresh, matches);
    }

    /// <summary>
    /// "model code BL32 ×3, landed A$41,200 > A$35,000 ×1": answers "why didn't anything match?" from the logs,
    /// in the one line per run that's already written. The most common reasons first, capped to keep it short.
    /// </summary>
    internal static string SummariseRejections(IReadOnlyDictionary<string, int> rejected, int max = 5)
    {
        if (rejected.Count == 0) return "none";
        var top = rejected.OrderByDescending(r => r.Value).ThenBy(r => r.Key, StringComparer.Ordinal).Take(max)
            .Select(r => $"{r.Key} ×{r.Value}");
        var more = rejected.Count > max ? $" (+{rejected.Count - max} more)" : "";
        return string.Join(", ", top) + more;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Key} rejected for '{Watchlist}': {Reasons}")]
    private static partial void LogRejected(ILogger logger, ListingKey key, string watchlist, IReadOnlyList<string> reasons);

    [LoggerMessage(Level = LogLevel.Information, Message = "Collected '{Watchlist}': {Seen} listings ({New} new), {Matched} matched; rejected: {Rejections}")]
    private static partial void LogCollected(ILogger logger, string watchlist, int seen, int @new, int matched, string rejections);
}
