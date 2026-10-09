using System.Globalization;

namespace Mitsuke.Core;

/// <summary>
/// Spotting one car listed twice. TheCarApi merges several Japanese feeds and sometimes keeps both copies of a lot:
/// on 8 Oct 2026 the same 27,000 km M3 was lot 58212 at "USS Nagoya" (no price) and at "USS Nagoya Hokuriku"
/// (¥3,500,000), with no frame number on either. The frame number links relists across weeks; this links copies of
/// the same lot on the same day.
/// </summary>
public static class ListingIdentity
{
    /// <summary>
    /// Auction day + lot number + chassis code + year + mileage, or null when the lot number or day is missing. The venue
    /// is left out on purpose: the duplicate feeds disagree about it.
    /// </summary>
    public static string? SameLotKey(Listing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);
        if (listing.LotNumber is not { Length: > 0 } lot || AlertFormatter.AuctionDay(listing) is not { } day) return null;
        return string.Join('|',
            day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), lot.Trim(), listing.ModelCode?.ToUpperInvariant() ?? "",
            listing.Year?.ToString(CultureInfo.InvariantCulture) ?? "", listing.MileageKm?.ToString(CultureInfo.InvariantCulture) ?? "");
    }

    /// <summary>
    /// The copy to keep: one with a price, then the one with more photos, then the lowest source id, so every run picks
    /// the same one and a car is never alerted twice through its two copies.
    /// </summary>
    public static IEnumerable<T> OnePerCar<T>(IEnumerable<T> items, Func<T, Listing> listing)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(listing);
        return items
            .GroupBy(i => SameLotKey(listing(i)) ?? $"own:{listing(i).Key}")
            .Select(g => g
                .OrderByDescending(i => listing(i).Price is not null)
                .ThenByDescending(i => listing(i).PhotoUrls.Count)
                .ThenBy(i => listing(i).Key.SourceId, StringComparer.Ordinal)
                .First());
    }
}
