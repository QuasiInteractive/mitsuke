namespace Mitsuke.Core;

/// <summary>A user's saved wishlist car.</summary>
public sealed record Watchlist
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Make { get; init; }
    public required string Model { get; init; }

    /// <summary>Accepted chassis codes, e.g. ["BNR32"] to get GT-Rs only. Empty means any.</summary>
    public IReadOnlySet<string> ModelCodes { get; init; } = new HashSet<string>();

    public int? YearFrom { get; init; }
    public int? YearTo { get; init; }
    public int? MaxMileageKm { get; init; }
    public decimal? MinGrade { get; init; }
    public bool IncludeRepaired { get; init; }
    public bool IncludeModified { get; init; } = true;

    /// <summary>Price ceiling in the listing's own currency (an opening-bid limit). Most people want <see cref="MaxLanded"/>.</summary>
    public Money? MaxPrice { get; init; }

    /// <summary>Where the car is going, ISO country code ("AU", "NZ"). Decides the landed-cost rules and currency.</summary>
    public string Destination { get; init; } = "AU";

    /// <summary>Budget on the ground in the buyer's currency, compared against the estimate's midpoint.</summary>
    public Money? MaxLanded { get; init; }

    public bool IsActive { get; init; } = true;

    /// <summary>The user it belongs to; null for system/demo watchlists.</summary>
    public Guid? OwnerId { get; init; }

    /// <summary>
    /// Most chassis codes searched one by one (one request each) before it's cheaper to search the whole model.
    /// </summary>
    public const int MaxCodesSearchedSeparately = 3;

    /// <summary>
    /// What to ask the data source for: this watchlist's chassis codes and mileage, cheapest first, one page. The data
    /// key allows a few hundred requests a day (Oct 2026), so a search is one request, not five pages of every
    /// Skyline to keep the BNR32s.
    /// </summary>
    public SourceQuery ToSourceQuery() => new(Make, Model, YearFrom, YearTo,
        ModelCodes.Count is > 0 and <= MaxCodesSearchedSeparately ? [.. ModelCodes.Order(StringComparer.OrdinalIgnoreCase)] : null,
        MaxMileageKm, CheapestFirst: true, Limit: 50);
}
