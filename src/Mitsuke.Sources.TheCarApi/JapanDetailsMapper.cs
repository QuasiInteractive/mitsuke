using System.Globalization;
using System.Text.Json;
using Mitsuke.Core;

namespace Mitsuke.Sources.TheCarApi;

internal static class JapanDetailsMapper
{
    public static ListingDetails Map(ListingKey key, AuctionDetail auction, Uri baseAddress, DateTimeOffset fetchedAt)
    {
        var ci = auction.CarIdentification;

        var sheets = (ci?.InspectionReports ?? [])
            .Where(r => r.Url is not null && r.Type is "auction_sheet" or "previous_auction_sheet")
            .Select(r => new AuctionSheet(new Uri(baseAddress, r.Url), ParseDate(r.AuctionDate), r.Type == "auction_sheet"))
            .ToList();

        var relists = (ci?.JapanRelists ?? [])
            .Select(r => ParseDate(r.AuctionDate) is { } date
                ? new Relist(
                    date,
                    JapanListingMapper.AuctionHouseName(r.Venue),
                    r.LotNumber,
                    AuctionGrade.Parse(r.AuctionGrade),
                    r.Fields?.MileageKm is { } km ? (int)Math.Round(km) : null,
                    r.OpeningPrice is { Amount: { } amount, Currency: { } currency } ? new Money(amount, currency.ToUpperInvariant()) : null,
                    r.Match,
                    (r.Changes ?? []).Select(Describe).ToList())
                : null)
            .OfType<Relist>()
            .OrderByDescending(r => r.AuctionDate)
            .ToList();

        return new ListingDetails
        {
            Key = key,
            FetchedAt = fetchedAt,
            Sheets = sheets,
            Relists = relists,
            InteriorGrade = string.IsNullOrWhiteSpace(ci?.InteriorGrade) ? null : ci.InteriorGrade.Trim(),
            EngineCc = auction.CylinderCapacity,
            PhotoUrls = (auction.Images ?? [])
                .Select(i => i.ServedUrl ?? i.Url)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => new Uri(baseAddress, u))
                .ToList(),
        };
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    // A change is either a field name or an object naming one; anything else is kept as compact JSON.
    private static string Describe(JsonElement change) => change.ValueKind switch
    {
        JsonValueKind.String => change.GetString() ?? "",
        JsonValueKind.Object when change.TryGetProperty("field", out var f) && f.ValueKind == JsonValueKind.String => f.GetString() ?? "",
        _ => change.GetRawText(),
    };
}
