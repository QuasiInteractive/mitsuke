namespace Mitsuke.Core;

/// <summary>Identifies one listing at one source. A relisted car gets a new key; see <see cref="Listing.FrameNumber"/>.</summary>
public readonly record struct ListingKey(string Source, string SourceId)
{
    public override string ToString() => $"{Source}/{SourceId}";
}

public readonly record struct Money(decimal Amount, string Currency)
{
    public override string ToString() => $"{Amount:N0} {Currency}";
}

/// <summary>What a listed price actually means. Japanese auction prices are opening bids, never sale prices.</summary>
public enum PriceKind
{
    Unknown,
    OpeningBid,
    AskingPrice,
    ReportedFinal,
}

/// <summary>
/// The normalised car listing every source adapter produces. Nothing outside an adapter
/// should know which provider a listing came from, apart from <see cref="Attribution"/>.
/// </summary>
public sealed record Listing
{
    public required ListingKey Key { get; init; }
    public required string Make { get; init; }
    public required string Model { get; init; }

    /// <summary>Chassis model code without modification suffixes, e.g. "BNR32".</summary>
    public string? ModelCode { get; init; }

    /// <summary>True when the source marks the car as modified (Japanese "カイ" suffix on the model code).</summary>
    public bool IsModified { get; init; }

    /// <summary>Japanese frame number, e.g. "BNR32-305737". JDM cars have no VIN; this is the stable identity when present.</summary>
    public string? FrameNumber { get; init; }

    public int? Year { get; init; }

    /// <summary>Month of manufacture (1–12) when the source gives it, alongside a production <see cref="Year"/>.</summary>
    public int? Month { get; init; }
    public int? MileageKm { get; init; }
    public AuctionGrade? Grade { get; init; }
    public Money? Price { get; init; }
    public PriceKind PriceKind { get; init; }

    public string? AuctionHouse { get; init; }
    public string? LotNumber { get; init; }
    /// <summary>End of the auction day (midnight in Japan). Sources give the day, not a hammer time.</summary>
    public DateTimeOffset? AuctionEndsAt { get; init; }

    public string? Transmission { get; init; }
    public string? Fuel { get; init; }
    public bool? RightHandDrive { get; init; }

    public IReadOnlyList<Uri> PhotoUrls { get; init; } = [];

    /// <summary>Human-readable source credit shown on every alert, e.g. "USS Tokyo auction via TheCarApi".</summary>
    public required string Attribution { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }
}
