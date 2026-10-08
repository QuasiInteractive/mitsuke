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
/// <summary>Where alerts link to: the web app's lot page. Absent means alerts carry no link (e.g. no web app yet).</summary>
public sealed record AlertLinks(Uri WebBaseUrl)
{
    public Uri LotUrl(Guid listingId) => new(WebBaseUrl, $"lot/{listingId}");

    public Uri ManageUrl => new(WebBaseUrl, "watchlists");
}

/// <summary>One or more channels failed for an alert; the inner exception is the first failure.</summary>
public sealed class AlertDeliveryException(string message, Exception inner) : Exception(message, inner);

public sealed partial class AlertSender(
    IListingStore listings,
    IWatchlistStore watchlists,
    IAlertLog alerts,
    IEnumerable<IListingDetailsSource> detailSources,
    IListingDetailsStore detailsStore,
    ILandedCostEstimator landedCost,
    IComparablesStore comparables,
    IEnumerable<ISheetDecoder> sheetDecoders,
    ISheetReportStore sheetReports,
    INotifier notifier,
    TimeProvider clock,
    ILogger<AlertSender> logger,
    AlertLinks? links = null,
    IEmailSender? email = null,
    IUserStore? users = null,
    IPushSender? push = null,
    IPushSubscriptionStore? pushSubscriptions = null,
    IEligibilityChecker? eligibility = null)
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

        // Channels: a person's own watchlist goes to their email and each device with notifications on;
        // system/demo watchlists go to the shared channel (Discord/console).
        var channels = new List<string>();
        User? recipient = null;
        IReadOnlyList<PushSubscription> devices = [];
        if (watchlist.OwnerId is { } owner && users is not null && await users.GetAsync(owner, cancellationToken) is { } user)
        {
            recipient = user;
            if (email is not null) channels.Add("email");
            if (push is not null && pushSubscriptions is not null)
            {
                devices = await pushSubscriptions.GetForUserAsync(owner, cancellationToken);
                if (devices.Count > 0) channels.Add("push");
            }
        }
        if (channels.Count == 0) channels.Add(notifier.Channel);

        // Enrichment (details, sheet, score) is shared by every channel and only computed if one actually sends.
        Enriched? enriched = null;
        async Task<Enriched> EnrichAsync()
        {
            if (enriched is not null) return enriched;
            var landed = listing.Price is { } price ? await landedCost.EstimateAsync(price, watchlist.Destination, cancellationToken) : null;
            // Details cost one request per car, so they're fetched only for alerts actually going out.
            var details = await GetDetailsAsync(request.ListingId, listing.Key, cancellationToken);
            var deal = DealScorer.Score(listing, await comparables.GetCandidatesAsync(listing, cancellationToken: cancellationToken));
            var sheet = await GetSheetReportAsync(request.ListingId, details?.CurrentSheet, cancellationToken);
            // Local rules, no network: "can I even import it?" belongs in every alert.
            var import = eligibility is null ? null : await eligibility.CheckAsync(listing, watchlist.Destination, sheet?.Modifications, cancellationToken);
            return enriched = new Enriched(landed, details, deal, sheet, import, links?.LotUrl(request.ListingId));
        }

        // Each channel is claimed on its own: a retry after a failed push resends only the push, never the email.
        int sent = 0, already = 0;
        Exception? firstFailure = null;
        foreach (var channel in channels)
        {
            if (await alerts.TryClaimAsync(watchlist.Id, request.ListingId, channel, cancellationToken) is not { } alertId)
            {
                already++;
                continue;
            }

            try
            {
                var e = await EnrichAsync();
                switch (channel)
                {
                    case "email":
                        await email!.SendAsync(
                            AlertEmailFormatter.Format(recipient!.Email, watchlist, listing, e.Details, e.Landed, e.Deal, e.Sheet, e.LotUrl, links?.ManageUrl, e.Import),
                            cancellationToken);
                        break;
                    case "push":
                        await PushToDevicesAsync(devices, PushFormatter.Format(listing, e.Landed, e.Deal, e.Sheet, e.LotUrl, e.Import, e.Details), cancellationToken);
                        break;
                    default:
                        await notifier.SendAsync(AlertFormatter.Format(watchlist, listing, e.Details, e.Landed, e.Deal, e.Sheet, e.LotUrl, e.Import), cancellationToken);
                        break;
                }

                await alerts.MarkSentAsync(alertId, cancellationToken);
                LogSent(logger, listing.Key, watchlist.Name, channel);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // CancellationToken.None: recording the failure must happen even if the caller is shutting down.
                await alerts.MarkFailedAsync(alertId, ex.Message, CancellationToken.None);
                LogSendFailed(logger, ex, listing.Key, channel);
                firstFailure ??= ex;
            }
        }

        // Any failure throws so the queue redelivers; the claims make sure only the failed channels go again.
        if (firstFailure is not null) throw new AlertDeliveryException($"{channels.Count - sent - already} channel(s) failed", firstFailure);
        return sent > 0 ? AlertOutcome.Sent : AlertOutcome.AlreadySent;
    }

    private sealed record Enriched(LandedEstimate? Landed, ListingDetails? Details, DealScore? Deal, SheetReport? Sheet, ImportEligibility? Import, Uri? LotUrl);

    /// <summary>
    /// Sends to every device; a device the push service says is gone is deleted. Succeeds if any device got it,
    /// or if every subscription was simply gone (nothing left to retry).
    /// </summary>
    private async Task PushToDevicesAsync(IReadOnlyList<PushSubscription> devices, PushMessage message, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        var delivered = 0;
        var gone = 0;
        foreach (var device in devices)
        {
            try
            {
                if (await push!.SendAsync(device, message, cancellationToken) == PushResult.Gone)
                {
                    await pushSubscriptions!.RemoveAsync(device.Id, cancellationToken);
                    gone++;
                }
                else
                {
                    await pushSubscriptions!.MarkDeliveredAsync(device.Id, cancellationToken);
                    delivered++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }
        if (delivered == 0 && gone < devices.Count && lastError is not null) throw lastError;
    }

    /// <summary>
    /// The decoded sheet: cached forever once read (each read is a paid AI call), decoded on first need otherwise.
    /// Optional by design: no decoder configured, no sheet, or a decoding failure all just mean an alert without it.
    /// </summary>
    private async Task<SheetReport?> GetSheetReportAsync(Guid listingId, AuctionSheet? sheet, CancellationToken cancellationToken)
    {
        var cached = await sheetReports.GetAsync(listingId, cancellationToken);
        if (cached is not null && (sheet is null || cached.SheetUrl == sheet.ImageUrl)) return cached;
        if (sheet is null || sheetDecoders.FirstOrDefault() is not { } decoder) return cached;

        try
        {
            var report = await decoder.DecodeAsync(sheet.ImageUrl, cancellationToken);
            if (report is null) return cached;
            await sheetReports.SaveAsync(listingId, report, cancellationToken);
            LogSheetDecoded(logger, listingId, report.RedFlags.Count, report.CostUsd);
            return report;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSheetFailed(logger, ex, listingId);
            return cached;
        }
    }

    /// <summary>Cached details if fresh; otherwise fetch and cache. A failed fetch degrades to cached-or-none, never blocks the alert.</summary>
    private async Task<ListingDetails?> GetDetailsAsync(Guid listingId, ListingKey key, CancellationToken cancellationToken)
    {
        var cached = await detailsStore.GetAsync(listingId, cancellationToken);
        if (DetailsRefresher.IsCurrent(cached) && clock.GetUtcNow() - cached!.FetchedAt < DetailsMaxAge) return cached;

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Decoded sheet for listing {ListingId}: {Flags} red flags, US${Cost}")]
    private static partial void LogSheetDecoded(ILogger logger, Guid listingId, int flags, decimal cost);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't decode the sheet for listing {ListingId}; alerting without it")]
    private static partial void LogSheetFailed(ILogger logger, Exception ex, Guid listingId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Alerted {Key} for '{Watchlist}' via {Channel}")]
    private static partial void LogSent(ILogger logger, ListingKey key, string watchlist, string channel);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped alert: watchlist {WatchlistId} or listing {ListingId} is gone or inactive")]
    private static partial void LogSkipped(ILogger logger, Guid watchlistId, Guid listingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't fetch details for {Key}; alerting without them")]
    private static partial void LogDetailsFailed(ILogger logger, Exception ex, ListingKey key);

    [LoggerMessage(Level = LogLevel.Error, Message = "Alert for {Key} via {Channel} failed; it will be retried")]
    private static partial void LogSendFailed(ILogger logger, Exception ex, ListingKey key, string channel);
}
