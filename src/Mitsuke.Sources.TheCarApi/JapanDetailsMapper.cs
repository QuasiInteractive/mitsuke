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
            Format = ListingDetails.CurrentFormat,
            Sheets = sheets,
            Relists = relists,
            InteriorGrade = string.IsNullOrWhiteSpace(ci?.InteriorGrade) ? null : ci.InteriorGrade.Trim(),
            EngineCc = auction.CylinderCapacity,
            Spec = Spec(auction),
            ManualGears = Shift(Ocr(ci, "shift")).ManualGears,
            PhotoUrls = (auction.Images ?? [])
                .Select(i => i.ServedUrl ?? i.Url)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => new Uri(baseAddress, u))
                .ToList(),
        };
    }

    private static readonly System.Globalization.CultureInfo Au = System.Globalization.CultureInfo.GetCultureInfo("en-AU");

    private static string? Text(Dictionary<string, JsonElement>? fields, string name) =>
        fields is not null && fields.TryGetValue(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            && (v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.GetString()) is { Length: > 0 } s ? s.Trim() : null;

    private static string? Ocr(CarIdentification? ci, string name) =>
        ci?.SheetOcr is { Status: "read" } ocr ? Text(ocr.Fields, name) : null;

    private static bool Validated(CarIdentification? ci, string name) => ci?.SheetOcr?.Validated?.Contains(name) == true;

    /// <summary>
    /// The extra facts for the lot page, best source first: TheCarApi's merged fields, then its sheet OCR. Each OCR
    /// value is marked unchecked unless TheCarApi validated that field.
    /// </summary>
    private static List<SpecItem> Spec(AuctionDetail auction)
    {
        var ci = auction.CarIdentification;
        var spec = new List<SpecItem>();
        void Add(string label, string? value, bool isChecked)
        {
            if (!string.IsNullOrWhiteSpace(value)) spec.Add(new SpecItem(label, value, isChecked));
        }

        Add("Colour", Text(ci?.JapanMerged?.Fields, "color") is { } c ? Au.TextInfo.ToTitleCase(c.ToLowerInvariant()) : null, true);
        Add("Engine", auction.CylinderCapacity is { } cc ? string.Create(Au, $"{cc:N0} cc") : null, true);
        Add("Gearbox", Shift(Ocr(ci, "shift")).Label, Validated(ci, "shift"));
        Add("First registered in Japan", Month(Ocr(ci, "first_registration")), Validated(ci, "first_registration"));
        Add("Full model code", Ocr(ci, "model_code"), Validated(ci, "model_code"));
        Add("Doors", Ocr(ci, "doors"), Validated(ci, "doors"));
        Add("Seats", Ocr(ci, "seats"), Validated(ci, "seats"));
        Add("Air conditioning", Ocr(ci, "ac") switch
        {
            "AAC" => "Climate control",
            "AC" => "Air conditioning",
            "WAC" => "Dual-zone air conditioning",
            _ => null,
        }, Validated(ci, "ac"));
        Add("Japanese recycling fee", int.TryParse(Ocr(ci, "recycle_fee_jpy"), out var fee) && fee > 0 ? string.Create(Au, $"¥{fee:N0} (paid, usually refunded to the seller)") : null, Validated(ci, "recycle_fee_jpy"));
        return spec;
    }

    /// <summary>Japanese sheet shift codes: F6 is a 6-speed floor-shift manual, FAT a floor automatic, CAT a column automatic.</summary>
    internal static (string? Label, int? ManualGears) Shift(string? code) => code?.ToUpperInvariant() switch
    {
        null => (null, null),
        ['F' or 'C', var n] when n is >= '4' and <= '7' => ($"{n - '0'}-speed manual{(code[0] is 'F' or 'f' ? ", floor shift" : ", column shift")}", n - '0'),
        "FAT" or "CAT" or "IAT" or "DAT" or "AT" => ("Automatic", null),
        "CVT" or "FCVT" or "CCVT" => ("CVT automatic", null),
        _ => (null, null),
    };

    /// <summary>"2003-02" as "Feb 2003".</summary>
    private static string? Month(string? yearMonth) =>
        DateTime.TryParseExact(yearMonth, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("MMM yyyy", Au)
            : null;

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
