namespace Mitsuke.Core;

/// <summary>One line of a landed-cost estimate, e.g. shipping or GST. Low/High are set when the line is a range.</summary>
public sealed record CostLine(string Id, string Label, Money Amount, Money? Low, Money? High, bool IsEstimate, string? Note);

/// <summary>
/// What a car would cost on the ground in the buyer's country. Always an estimate: Japanese prices are opening
/// bids, and fees and freight vary. <see cref="Low"/>–<see cref="High"/> is the honest range; <see cref="Total"/> its midpoint.
/// </summary>
public sealed record LandedEstimate
{
    public required string Destination { get; init; }
    public required Money Total { get; init; }
    public required Money Low { get; init; }
    public required Money High { get; init; }

    /// <summary>Yen per one unit of the destination currency used for the conversion.</summary>
    public required decimal JpyPerUnit { get; init; }

    public string? FxDate { get; init; }
    public bool FxLive { get; init; }
    public IReadOnlyList<CostLine> Lines { get; init; } = [];
    public string? Note { get; init; }
}

public interface ILandedCostEstimator
{
    /// <summary>Null when the price can't be estimated (not in yen, or no rules for the destination).</summary>
    Task<LandedEstimate?> EstimateAsync(Money price, string destination, CancellationToken cancellationToken = default);
}
