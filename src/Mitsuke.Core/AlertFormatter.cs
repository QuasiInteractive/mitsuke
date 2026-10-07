using System.Globalization;
using System.Text;

namespace Mitsuke.Core;

/// <summary>The short, scannable alert text. The full report lives on the lot page this will link to.</summary>
public static class AlertFormatter
{
    public const string Disclaimer = "Estimates only. Verify the car and all costs before bidding.";

    private static readonly TimeSpan Jst = TimeSpan.FromHours(9);
    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");

    public static string Format(
        Watchlist watchlist, Listing listing, ListingDetails? details = null, LandedEstimate? landed = null, DealScore? deal = null,
        SheetReport? sheet = null, Uri? lotUrl = null)
    {
        ArgumentNullException.ThrowIfNull(watchlist);
        ArgumentNullException.ThrowIfNull(listing);

        var title = string.Join(' ', new[] { listing.Year?.ToString(CultureInfo.InvariantCulture), listing.Make, listing.Model, listing.ModelCode }
            .Where(s => !string.IsNullOrEmpty(s)));

        var facts = new List<string>();
        if (listing.MileageKm is { } km) facts.Add(string.Create(Au, $"{km:N0} km"));
        if (listing.Grade is { } grade) facts.Add($"grade {grade}");
        if (listing.IsModified) facts.Add("modified");
        if (listing.Transmission is { } t) facts.Add(t.ToLowerInvariant());

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"[{watchlist.Name}] {title}");
        if (facts.Count > 0) sb.Append(". ").Append(string.Join(", ", facts));
        sb.AppendLine(".");

        // Serious problems from the sheet come before any price talk: a cheap car is often cheap for a reason.
        foreach (var flag in sheet?.RedFlags.Where(f => f.Severity == FlagSeverity.High) ?? [])
            sb.AppendLine(CultureInfo.InvariantCulture, $"⚠ {flag.Title}. {flag.Detail}");

        if (landed is not null)
        {
            var (total, low, high) = (landed.Total, landed.Low, landed.High);
            sb.AppendLine(string.Create(Au,
                $"Est. landed in {landed.Destination}: {Symbol(total.Currency)}{total.Amount:N0} (range {Symbol(low.Currency)}{low.Amount:N0}–{high.Amount:N0})"));
        }

        if (deal is { Score: { } score })
        {
            var confidence = deal.Confidence == ScoreConfidence.Low ? "; low confidence" : "";
            var vsTypical = deal.BelowTypical is { } diff && diff.Amount != 0
                ? string.Create(Au, $"; {Symbol(diff.Currency)}{Math.Abs(diff.Amount):N0} {(diff.Amount > 0 ? "under" : "over")} the typical opening bid")
                : "";
            sb.AppendLine(string.Create(Au, $"Deal score {score}/100 · {deal.Label} (vs {deal.Basis}{confidence}){vsTypical}."));
        }
        else if (deal is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Deal score: {deal.Label}.");
        }

        if (listing.Price is { } price)
            sb.AppendLine(string.Create(Au, $"{Label(listing.PriceKind)}: {Symbol(price.Currency)}{price.Amount:N0}"));
        else
            sb.AppendLine("No opening price published.");

        if (AuctionDay(listing) is { } day)
        {
            var lot = listing.LotNumber is null ? "" : $", lot {listing.LotNumber}";
            sb.AppendLine(string.Create(Au, $"Auction {day:ddd d MMM} (Japan){lot}"));
        }

        if (details is not null)
        {
            if (details.Relists.Count > 0)
            {
                var first = details.Relists.Min(r => r.AuctionDate);
                var times = details.Relists.Count == 1 ? "once" : $"{details.Relists.Count} times";
                sb.AppendLine(string.Create(Au, $"Seen at auction {times} before, since {first:d MMM}."));
            }

            var changed = details.Relists.SelectMany(r => r.Changes).Where(c => c.Length > 0).Distinct().ToList();
            if (changed.Count > 0) sb.AppendLine(CultureInfo.InvariantCulture, $"Check: {string.Join(", ", changed)} changed between auctions.");

            if (details.CurrentSheet is not null && sheet is null) sb.AppendLine("Auction sheet available (not yet decoded).");
        }

        if (sheet is not null)
        {
            if (sheet.Summary.Length > 0) sb.AppendLine(CultureInfo.InvariantCulture, $"Condition (from the auction sheet): {sheet.Summary}");
            var others = sheet.RedFlags.Where(f => f.Severity == FlagSeverity.Medium).Select(f => f.Title).ToList();
            if (others.Count > 0) sb.AppendLine(CultureInfo.InvariantCulture, $"Also check: {string.Join("; ", others)}.");
        }

        if (lotUrl is not null) sb.AppendLine(CultureInfo.InvariantCulture, $"View in Mitsuke → {lotUrl}");
        sb.AppendLine(listing.Attribution);
        sb.Append(Disclaimer);
        return sb.ToString();
    }

    /// <summary>
    /// TheCarApi gives the end of the auction day (midnight JST) rather than a hammer time,
    /// so we report the day itself, not a clock time we don't actually know.
    /// </summary>
    public static DateOnly? AuctionDay(Listing listing) =>
        listing?.AuctionEndsAt is { } end ? DateOnly.FromDateTime(end.ToOffset(Jst).AddSeconds(-1).DateTime) : null;

    private static string Label(PriceKind kind) => kind switch
    {
        PriceKind.OpeningBid => "Opening bid",
        PriceKind.AskingPrice => "Asking",
        PriceKind.ReportedFinal => "Reported result (unverified)",
        _ => "Price",
    };

    private static string Symbol(string currency) => currency switch
    {
        "JPY" => "¥",
        "AUD" => "A$",
        "NZD" => "NZ$",
        _ => currency + " ",
    };
}
