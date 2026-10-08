using System.Globalization;

namespace Mitsuke.Core;

public static class WatchlistMatcher
{
    public static bool IsMatch(Watchlist watchlist, Listing listing, DateTimeOffset? now = null, LandedEstimate? landed = null) =>
        Mismatches(watchlist, listing, now, landed).Count == 0;

    /// <summary>
    /// Every reason the listing fails the watchlist; empty means it matches.
    /// Unknown values (null mileage, ungraded) fail a filter that needs them, so we never alert on a guess.
    /// </summary>
    /// <param name="now">
    /// When given, listings whose auction day has already started are rejected. An alert nobody can act on is noise:
    /// bids go through an exporter and must be in before the day begins.
    /// </param>
    /// <param name="landed">The listing's landed estimate for the watchlist's destination; needed when it has a landed budget.</param>
    public static IReadOnlyList<string> Mismatches(Watchlist watchlist, Listing listing, DateTimeOffset? now = null, LandedEstimate? landed = null)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        ArgumentNullException.ThrowIfNull(listing);
        var reasons = new List<string>();

        if (!Same(watchlist.Make, listing.Make)) reasons.Add($"make {listing.Make}");

        // A chassis code identifies the car more precisely than the feed's model name, which varies (a CT9A Evo can be
        // listed as "Lancer" or "Lancer Evolution"). So when the watchlist names codes, the code decides; otherwise the model.
        if (watchlist.ModelCodes.Count > 0)
        {
            if (listing.ModelCode is null || !watchlist.ModelCodes.Contains(listing.ModelCode, StringComparer.OrdinalIgnoreCase))
                reasons.Add($"model code {listing.ModelCode ?? "unknown"}");
        }
        else if (!Same(watchlist.Model, listing.Model))
        {
            reasons.Add($"model {listing.Model}");
        }

        if (listing.IsModified && !watchlist.IncludeModified) reasons.Add("modified");

        if (watchlist.YearFrom is { } from && !(listing.Year >= from)) reasons.Add($"year {Show(listing.Year)} < {from}");
        if (watchlist.YearTo is { } to && !(listing.Year <= to)) reasons.Add($"year {Show(listing.Year)} > {to}");

        if (watchlist.MaxMileageKm is { } maxKm && !(listing.MileageKm <= maxKm))
            reasons.Add($"mileage {Show(listing.MileageKm, "N0")} km > {maxKm:N0}");

        if (listing.Grade?.IsRepaired == true && !watchlist.IncludeRepaired) reasons.Add($"repaired (grade {listing.Grade})");
        else if (watchlist.MinGrade is { } minGrade && !(listing.Grade?.Score >= minGrade))
            reasons.Add($"grade {listing.Grade?.Raw ?? "unknown"} < {minGrade}");

        if (watchlist.MaxPrice is { } max)
        {
            if (listing.Price is not { } price) reasons.Add("no price");
            else if (price.Currency != max.Currency) reasons.Add($"price in {price.Currency}, limit in {max.Currency}");
            else if (price.Amount > max.Amount) reasons.Add($"price {price} > {max}");
        }

        if (watchlist.MaxLanded is { } budget)
        {
            if (landed is null) reasons.Add("no landed estimate");
            else if (landed.Total.Currency != budget.Currency) reasons.Add($"landed in {landed.Total.Currency}, budget in {budget.Currency}");
            else if (landed.Total.Amount > budget.Amount) reasons.Add($"landed {landed.Total} > {budget}");
        }

        // AuctionEndsAt is the end of the auction day, so the day itself began 24 hours earlier.
        if (now is { } t && listing.AuctionEndsAt is { } ends && ends.AddDays(-1) <= t) reasons.Add("too late to bid");

        return reasons;
    }

    private static string Show(int? value, string format = "D") =>
        value?.ToString(format, CultureInfo.InvariantCulture) ?? "unknown";

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
