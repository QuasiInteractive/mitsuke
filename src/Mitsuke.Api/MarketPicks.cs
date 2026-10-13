using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Mitsuke.Core;

namespace Mitsuke.Api;

/// <summary>A car the market scan picked: the lot, and how far under its chassis code's typical opening bid it started.</summary>
public sealed record MarketPick(LotCard Lot, string Group, Money Typical, int PercentUnder, int Cars, bool BiddingOpen);

/// <summary>
/// The home page's top section. <see cref="Steal"/> is the cheapest-to-start clean car first seen this month (it may
/// have gone to auction already); <see cref="CheapNow"/> is what can still be bid on.
/// </summary>
public sealed record MarketPicks(string Month, MarketPick? Steal, IReadOnlyList<MarketPick> CheapNow, DateTimeOffset ScannedAt, string Basis);

/// <summary>
/// Runs the market scan and confirms each candidate with the full deal score (same mileage and grade band), so the
/// home page never calls something a steal that the lot page would call typical. Cached: it's the same for everyone.
/// </summary>
public sealed class MarketPicksBuilder(IMarketStore market, IListingStore listings, LotViewBuilder lots, IComparablesStore comparables,
    IMemoryCache cache, TimeProvider clock)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);
    private const int MinimumDealScore = 70;
    private const int MinimumPercentUnder = 15;

    public static readonly string Basis =
        "Opening bids against the typical opening bid for the same chassis code and gearbox, similar age, mileage and grade, " +
        "grade 3.5 or better, not repaired, not high mileage for the model. Opening bids, not sale prices.";

    public async Task<MarketPicks> GetAsync(string destination, CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync($"picks:{destination}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            return await BuildAsync(destination, cancellationToken);
        }))!;

    private async Task<MarketPicks> BuildAsync(string destination, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var tokyo = now.ToOffset(TimeSpan.FromHours(9));
        var monthStart = new DateTimeOffset(tokyo.Year, tokyo.Month, 1, 0, 0, 0, TimeSpan.FromHours(9));

        var picks = new List<MarketPick>();
        foreach (var c in await market.ScanAsync(monthStart, now, 24, cancellationToken))
        {
            if (await listings.GetAsync(c.ListingId, cancellationToken) is not { } listing) continue;
            var deal = DealScorer.Score(listing, await comparables.GetCandidatesAsync(listing, cancellationToken: cancellationToken));
            if (deal.Score is not { } score || score < MinimumDealScore) continue; // cheap for the code, not for its spec
            // The scan's typical mixes manuals and automatics; the deal score compares like with like, so its typical
            // is the honest one to quote: an automatic opening at an automatic's usual price is no steal.
            if (deal.Typical is not { } typical || listing.Price is not { } price || typical.Amount <= 0) continue;
            var under = (int)Math.Round(100m * (1m - price.Amount / typical.Amount), MidpointRounding.AwayFromZero);
            if (under < MinimumPercentUnder) continue;
            var gearbox = Gearbox.Class(listing.Transmission);
            var group = gearbox is null ? c.Group : $"{gearbox} {c.Group}";

            if (await lots.CardsAsync([new MatchSummary(c.ListingId, Guid.Empty, c.Group, c.FirstSeenAt)], destination, cancellationToken)
                is not [var card]) continue;
            var open = listing.AuctionEndsAt is not { } ends || ends - TimeSpan.FromDays(1) > now;
            picks.Add(new MarketPick(card, group, new Money(Math.Round(typical.Amount / 10_000m) * 10_000m, "JPY"), under, deal.ComparableCount, open));
            if (picks.Count(p => p.BiddingOpen) >= 6 && picks.Count >= 8) break;
        }

        var month = tokyo.ToString("MMMM", CultureInfo.InvariantCulture);
        // One you can still bid on beats a slightly better one that's gone; the month's best only when nothing's open.
        var steal = picks.Where(p => p.BiddingOpen).OrderByDescending(p => p.PercentUnder).FirstOrDefault()
            ?? picks.OrderByDescending(p => p.PercentUnder).FirstOrDefault();
        return new MarketPicks(month, steal,
            picks.Where(p => p.BiddingOpen).Take(6).ToList(), now, Basis);
    }
}
