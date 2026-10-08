using System.Text.Json.Serialization;

namespace Mitsuke.Sources.TheCarApi;

// Wire shapes for the fields we use, verified against live /api/search responses (see docs/thecarapi-findings.md).
// Everything is nullable: TheCarApi coverage varies by source and row.

internal sealed record SearchResponse
{
    public bool Success { get; init; }
    public long? Total { get; init; }
    public List<AuctionRow> Results { get; init; } = [];
}

internal sealed record AuctionRow
{
    // auction_id exceeds 2^53 for Japan; only ever read the string form.
    public string? AuctionIdStr { get; init; }
    public string? SiteName { get; init; }
    public string? CleanMake { get; init; }
    public string? CleanModel { get; init; }
    public string? ModelCode { get; init; }
    public string? FrameNumber { get; init; }
    public int? ProductionYear { get; init; }
    public int? ProductionMonth { get; init; }
    public int? RegistrationYear { get; init; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Mileage { get; init; }

    public string? AuctionGrade { get; init; }
    public string? AuctionHouse { get; init; }
    public string? LotNumber { get; init; }
    public DateTimeOffset? AuctionEndAt { get; init; }
    public string? GearboxGroup { get; init; }
    public string? FuelGroup { get; init; }
    public string? Steering { get; init; }
    public NativePrices? NativePrices { get; init; }
    public List<ImageRef>? Images { get; init; }
}

internal sealed record NativePrices
{
    public NativePrice? CurrentPrice { get; init; }
    public NativePrice? FinalPrice { get; init; }
}

internal sealed record NativePrice
{
    // Detail payloads send amounts as strings like "3.98E+6"; search sends numbers.
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Amount { get; init; }

    public string? Currency { get; init; }
}

internal sealed record ImageRef
{
    public string? ServedUrl { get; init; }
    public string? Url { get; init; }
}
