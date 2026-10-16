using System.Globalization;

namespace Mitsuke.Core;

/// <summary>The same physical car (same chassis number) at a later auction: this one didn't sell, or was resold.</summary>
public sealed record LaterAppearance(Guid ListingId, DateOnly? AuctionDay, string? AuctionHouse, string? LotNumber, Money? OpeningBid);

/// <summary>What Mitsuke has seen of one lot: when it was first and last seen, and whether the car came back.</summary>
public sealed record LotSightings(DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, bool HasChassisNumber, IReadOnlyList<LaterAppearance> Later);

public enum LotStatus
{
    /// <summary>Auction still ahead: Mitsuke re-checks the lot every run.</summary>
    Live,
    /// <summary>Auction over, too soon to say how it went.</summary>
    Waiting,
    /// <summary>The car turned up at a later auction, so it didn't sell (or was bought and resold).</summary>
    Relisted,
    /// <summary>Not back in a fortnight: most likely sold. Mitsuke can't see the price.</summary>
    ProbablySold,
    /// <summary>No chassis number in the feed, so the car can't be followed to another auction.</summary>
    CantFollow,
}

/// <summary>The lot page's "Tracking this car" card.</summary>
public sealed record LotTracking
{
    public required LotStatus Status { get; init; }
    public required string Headline { get; init; }
    public string? Detail { get; init; }
    public required DateTimeOffset FirstSeenAt { get; init; }
    public required DateTimeOffset LastSeenAt { get; init; }

    /// <summary>Opening-bid changes on this listing (the first price doesn't count).</summary>
    public required int PriceChanges { get; init; }

    /// <summary>Latest opening bid against the first one seen: -8 means it dropped 8%.</summary>
    public int? PriceChangePercent { get; init; }
    public IReadOnlyList<LaterAppearance> Later { get; init; } = [];
}

public static class LotTracker
{
    /// <summary>Most unsold Japanese lots are back at auction within a week or two.</summary>
    public static readonly TimeSpan RelistWindow = TimeSpan.FromDays(14);

    public static LotTracking Track(Listing listing, LotSightings seen, IReadOnlyList<PricePoint> prices, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(seen);
        ArgumentNullException.ThrowIfNull(prices);
        var en = CultureInfo.GetCultureInfo("en-AU");
        string Yen(Money m) => string.Create(en, $"¥{m.Amount:N0}");
        string Day(DateOnly d) => d.ToString("ddd d MMM", en);

        var yen = prices.Where(p => p.Price.Currency == "JPY").ToList();
        int? pricePercent = yen.Count >= 2 && yen[0].Price.Amount > 0
            ? (int)Math.Round(100m * (yen[^1].Price.Amount - yen[0].Price.Amount) / yen[0].Price.Amount, MidpointRounding.AwayFromZero)
            : null;
        var changes = Math.Max(0, yen.Count - 1);

        var day = AlertFormatter.AuctionDay(listing);
        var over = listing.AuctionEndsAt is { } ends && ends <= now;
        LotStatus status;
        string headline;
        string? detail = null;

        if (!over)
        {
            status = LotStatus.Live;
            headline = changes == 0
                ? $"Opening bid unchanged since Mitsuke first saw it ({Yen(listing.Price ?? new Money(0, "JPY"))})."
                : $"Opening bid changed {changes} time{(changes == 1 ? "" : "s")}: {Yen(yen[0].Price)} to {Yen(yen[^1].Price)}.";
            detail = "Checked every hour until the auction. Live bidding isn't published outside the auction house, so nobody can see how it's going until the result.";
            if (listing.Price is null) headline = "No opening bid published yet.";
        }
        else if (seen.Later.Count > 0)
        {
            status = LotStatus.Relisted;
            var next = seen.Later[0];
            var change = next.OpeningBid is { Currency: "JPY" } nb && listing.Price is { Currency: "JPY", Amount: > 0 } p
                ? (int)Math.Round(100m * (nb.Amount - p.Amount) / p.Amount, MidpointRounding.AwayFromZero)
                : (int?)null;
            headline = $"Didn't sell{(day is { } d ? $" on {Day(d)}" : "")}: back at {next.AuctionHouse ?? "auction"}{(next.AuctionDay is { } nd ? $" on {Day(nd)}" : "")}.";
            detail = next.OpeningBid is { } ob
                ? $"Opening bid {Yen(ob)}{change switch { null => "", < 0 => $", {-change}% lower", > 0 => $", {change}% higher", _ => ", the same" }}. A car that comes back usually has a reserve above its opening bid, but sellers often lower it each time."
                : null;
        }
        else if (!seen.HasChassisNumber)
        {
            status = LotStatus.CantFollow;
            headline = "Auction over. Mitsuke can't follow this car to another auction.";
            detail = "The feed didn't give its chassis number, which is how a relisted car is recognised.";
        }
        else if (now - listing.AuctionEndsAt!.Value < RelistWindow)
        {
            status = LotStatus.Waiting;
            headline = "Auction over. If it didn't sell, it's usually back within a week or two, and Mitsuke will spot it.";
        }
        else
        {
            status = LotStatus.ProbablySold;
            headline = $"Not back at auction in {(int)(now - listing.AuctionEndsAt!.Value).TotalDays} days: most likely sold.";
            detail = "Japanese auctions don't publish results, so Mitsuke can't see what it went for. Results reported by exporter partners will fill this in.";
        }

        return new LotTracking
        {
            Status = status,
            Headline = headline,
            Detail = detail,
            FirstSeenAt = seen.FirstSeenAt,
            LastSeenAt = seen.LastSeenAt,
            PriceChanges = changes,
            PriceChangePercent = pricePercent,
            Later = seen.Later,
        };
    }
}
