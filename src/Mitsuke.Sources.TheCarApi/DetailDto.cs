using System.Text.Json.Serialization;

namespace Mitsuke.Sources.TheCarApi;

// GET /api/auction/japan/{id}. Verified against a live response (docs/thecarapi-findings.md).
// Note the mixed casing: the auction row is snake_case, car_identification's own keys are PascalCase.

internal sealed record DetailResponse
{
    public bool Success { get; init; }
    public AuctionDetail? Auction { get; init; }
}

internal sealed record AuctionDetail
{
    public string? AuctionIdStr { get; init; }
    public int? CylinderCapacity { get; init; }
    public List<ImageRef>? Images { get; init; }
    public CarIdentification? CarIdentification { get; init; }
}

internal sealed record CarIdentification
{
    [JsonPropertyName("InspectionReports")]
    public List<InspectionReport>? InspectionReports { get; init; }

    [JsonPropertyName("JapanRelists")]
    public List<RelistDto>? JapanRelists { get; init; }

    [JsonPropertyName("InteriorGrade")]
    public string? InteriorGrade { get; init; }

    [JsonPropertyName("JapanMerged")]
    public JapanMerged? JapanMerged { get; init; }

    [JsonPropertyName("SheetOcr")]
    public SheetOcr? SheetOcr { get; init; }
}

/// <summary>TheCarApi's merge of several feeds for the lot: the most reliable descriptive fields.</summary>
internal sealed record JapanMerged
{
    [JsonPropertyName("Fields")]
    public Dictionary<string, System.Text.Json.JsonElement>? Fields { get; init; }
}

/// <summary>
/// TheCarApi's own OCR of the auction sheet. Noisy (docs/thecarapi-findings.md): only the field names listed in
/// <see cref="Validated"/> were cross-checked; anything else is shown as "per the sheet".
/// </summary>
internal sealed record SheetOcr
{
    public string? Status { get; init; }
    public Dictionary<string, System.Text.Json.JsonElement>? Fields { get; init; }
    public List<string>? Validated { get; init; }
}

internal sealed record InspectionReport
{
    public string? Type { get; init; }
    public string? Url { get; init; }
    public string? AuctionDate { get; init; }
}

internal sealed record RelistDto
{
    public string? AuctionDate { get; init; }
    public string? AuctionGrade { get; init; }
    public string? Venue { get; init; }
    public string? LotNumber { get; init; }
    public string? Match { get; init; }
    public NativePrice? OpeningPrice { get; init; }
    public RelistFields? Fields { get; init; }

    // Shape of individual changes isn't documented; keep them raw and describe them generically.
    public List<System.Text.Json.JsonElement>? Changes { get; init; }
}

internal sealed record RelistFields
{
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? MileageKm { get; init; }
}
