using System.Text.Json.Serialization;

namespace Mitsuke.Core;

/// <summary>An auction sheet image. Previous sheets come from earlier auctions of the same car.</summary>
public sealed record AuctionSheet(Uri ImageUrl, DateOnly? AuctionDate, bool IsCurrent);

/// <summary>An earlier appearance of the same car at auction, as matched by the source.</summary>
/// <param name="Confidence">How sure the source is it's the same car, e.g. "probable". Shown, never hidden.</param>
/// <param name="Changes">Fields the source saw change since that appearance (e.g. mileage). A change is worth flagging.</param>
public sealed record Relist(
    DateOnly AuctionDate,
    string? AuctionHouse,
    string? LotNumber,
    AuctionGrade? Grade,
    int? MileageKm,
    Money? OpeningBid,
    string? Confidence,
    IReadOnlyList<string> Changes);

/// <summary>One fact about the car. <paramref name="Checked"/> is false for values machine-read from the sheet and not cross-checked.</summary>
public sealed record SpecItem(string Label, string Value, bool Checked);

/// <summary>
/// The expensive, per-car detail behind a listing: sheets, relist history, full gallery.
/// Fetched only for watchlist matches (one request per car) and cached.
/// </summary>
public sealed record ListingDetails
{
    /// <summary>
    /// Bumped when the mapper starts filling a new field. Saved details from an older format are refreshed in the
    /// background (<see cref="DetailsRefresher"/>). 0 = before formats existed; 2 = spec list and gearbox.
    /// </summary>
    public const int CurrentFormat = 2;

    public required ListingKey Key { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
    public int Format { get; init; }
    public IReadOnlyList<AuctionSheet> Sheets { get; init; } = [];
    public IReadOnlyList<Relist> Relists { get; init; } = [];
    public string? InteriorGrade { get; init; }
    public int? EngineCc { get; init; }
    public IReadOnlyList<Uri> PhotoUrls { get; init; } = [];

    /// <summary>Everything else known about the car (colour, gearbox, registration...), in display order.</summary>
    public IReadOnlyList<SpecItem> Spec { get; init; } = [];

    /// <summary>Forward gears of a manual gearbox, from the sheet's shift code (F6 is 6); null for automatics or unknown.</summary>
    public int? ManualGears { get; init; }

    [JsonIgnore]
    public AuctionSheet? CurrentSheet => Sheets.FirstOrDefault(s => s.IsCurrent);
}

/// <summary>A source that can fetch per-listing detail. Separate from <see cref="IListingSource"/>: not every source has one.</summary>
public interface IListingDetailsSource
{
    bool CanFetch(ListingKey key);

    Task<ListingDetails?> GetDetailsAsync(ListingKey key, CancellationToken cancellationToken = default);
}

public interface IListingDetailsStore
{
    Task<ListingDetails?> GetAsync(Guid listingId, CancellationToken cancellationToken = default);

    Task SaveAsync(Guid listingId, ListingDetails details, CancellationToken cancellationToken = default);
}
