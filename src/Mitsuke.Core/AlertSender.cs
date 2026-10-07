using Microsoft.Extensions.Logging;

namespace Mitsuke.Core;

public enum AlertOutcome
{
    Sent,

    /// <summary>Already delivered (or being delivered by someone else) on this channel.</summary>
    AlreadySent,

    /// <summary>The watchlist or listing no longer exists, or the watchlist was switched off.</summary>
    Skipped,
}

/// <summary>
/// Stage 2 of the pipeline for one match: claim it in the alert log, enrich it (details, landed cost, deal score)
/// and deliver it. A failed send is recorded and the exception rethrown, so the caller's retry mechanism
/// (a queue's redelivery, or the next scan) tries again; the claim makes a repeat delivery impossible.
/// </summary>
public sealed partial class AlertSender(
    IListingStore listings,
    IWatchlistStore watchlists,
    IAlertLog alerts,
    IEnumerable<IListingDetailsSource> detailSources,
    IListingDetailsStore detailsStore,
    ILandedCostEstimator landedCost,
    IComparablesStore comparables,
    INotifier notifier,
    TimeProvider clock,
    ILogger<AlertSender> logger)
{
    /// <summary>Cached details younger than this are reused rather than refetched.</summary>
    public static readonly TimeSpan DetailsMaxAge = TimeSpan.FromHours(24);

    public async Task<AlertOutcome> SendAsync(AlertRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var watchlist = await watchlists.GetAsync(request.WatchlistId, cancellationToken);
        var listing = await listings.GetAsync(request.ListingId, cancellationToken);
        if (watchlist is not { IsActive: true } || listing is null)
        {
            LogSkipped(logger, request.WatchlistId, request.ListingId);
            return AlertOutcome.Skipped;
        }

        if (await alerts.TryClaimAsync(watchlist.Id, request.ListingId, notifier.Channel, cancellationToken) is not { } alertId)
            return AlertOutcome.AlreadySent;

        try
        {
            var landed = listing.Price is { } price ? await landedCost.EstimateAsync(price, watchlist.Destination, cancellationToken) : null;
            // Details cost one request per car, so they're fetched only for alerts actually going out.
            var details = await GetDetailsAsync(request.ListingId, listing.Key, cancellationToken);
            var deal = DealScorer.Score(listing, await comparables.GetCandidatesAsync(listing, cancellationToken: cancellationToken));

            await notifier.SendAsync(AlertFormatter.Format(watchlist, listing, details, landed, deal), cancellationToken);
            await alerts.MarkSentAsync(alertId, cancellationToken);
            LogSent(logger, listing.Key, watchlist.Name, notifier.Channel);
            return AlertOutcome.Sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // CancellationToken.None: recording the failure must happen even if the caller is shutting down.
            await alerts.MarkFailedAsync(alertId, ex.Message, CancellationToken.None);
            LogSendFailed(logger, ex, listing.Key, notifier.Channel);
            throw;
        }
    }

    /// <summary>Cached details if fresh; otherwise fetch and cache. A failed fetch degrades to cached-or-none, never blocks the alert.</summary>
    private async Task<ListingDetails?> GetDetailsAsync(Guid listingId, ListingKey key, CancellationToken cancellationToken)
    {
        var cached = await detailsStore.GetAsync(listingId, cancellationToken);
        if (cached is not null && clock.GetUtcNow() - cached.FetchedAt < DetailsMaxAge) return cached;

        var source = detailSources.FirstOrDefault(s => s.CanFetch(key));
        if (source is null) return cached;

        try
        {
            var fetched = await source.GetDetailsAsync(key, cancellationToken);
            if (fetched is null) return cached;
            await detailsStore.SaveAsync(listingId, fetched, cancellationToken);
            return fetched;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDetailsFailed(logger, ex, key);
            return cached;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Alerted {Key} for '{Watchlist}' via {Channel}")]
    private static partial void LogSent(ILogger logger, ListingKey key, string watchlist, string channel);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped alert: watchlist {WatchlistId} or listing {ListingId} is gone or inactive")]
    private static partial void LogSkipped(ILogger logger, Guid watchlistId, Guid listingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't fetch details for {Key}; alerting without them")]
    private static partial void LogDetailsFailed(ILogger logger, Exception ex, ListingKey key);

    [LoggerMessage(Level = LogLevel.Error, Message = "Alert for {Key} via {Channel} failed; it will be retried")]
    private static partial void LogSendFailed(ILogger logger, Exception ex, ListingKey key, string channel);
}
