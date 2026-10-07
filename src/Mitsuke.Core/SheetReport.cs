namespace Mitsuke.Core;

public enum FlagSeverity
{
    Info,
    Medium,
    High,
}

/// <summary>A rule-based warning about the car read from its auction sheet, e.g. "Mileage marked as doubtful".</summary>
public sealed record SheetFlag(FlagSeverity Severity, string Title, string Detail);

/// <summary>One damage mark from the sheet's car diagram, translated where the code is known.</summary>
/// <param name="Name">"Dent", "Scratch"... Null when the code isn't in the translation table (e.g. handwritten Japanese).</param>
public sealed record SheetDamage(string Code, string Location, string? Name, string? Size, string? Summary, string? Note);

/// <summary>
/// The auction sheet decoded into plain English (by Kensa-ya). Machine-read from a photo of a handwritten
/// form, so it can be wrong: <see cref="Unclear"/> lists what couldn't be read confidently.
/// </summary>
public sealed record SheetReport
{
    public required Uri SheetUrl { get; init; }
    public required DateTimeOffset DecodedAt { get; init; }
    public required bool IsAuctionSheet { get; init; }
    public required string Summary { get; init; }

    public IReadOnlyList<SheetFlag> RedFlags { get; init; } = [];
    public IReadOnlyList<SheetDamage> Damage { get; init; } = [];
    public string? OverallGrade { get; init; }
    public string? InteriorGrade { get; init; }
    public string? Colour { get; init; }
    public int? MileageKm { get; init; }
    public IReadOnlyList<string> Modifications { get; init; } = [];
    public IReadOnlyList<string> Positives { get; init; } = [];
    public IReadOnlyList<string> WatchOut { get; init; } = [];
    public IReadOnlyList<string> Unclear { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True when the sheet says the odometer can't be trusted (replaced, tampered, doubtful).</summary>
    public bool MileageIsDoubtful => RedFlags.Any(f => f.Severity == FlagSeverity.High
        && (f.Title.Contains("mileage", StringComparison.OrdinalIgnoreCase) || f.Title.Contains("odometer", StringComparison.OrdinalIgnoreCase)));

    /// <summary>What reading it cost, for the cost log.</summary>
    public string? Model { get; init; }
    public decimal CostUsd { get; init; }
}

public interface ISheetDecoder
{
    /// <summary>Reads a sheet image. Null when the image isn't readable as an auction sheet at all.</summary>
    Task<SheetReport?> DecodeAsync(Uri sheetUrl, CancellationToken cancellationToken = default);
}

public interface ISheetReportStore
{
    Task<SheetReport?> GetAsync(Guid listingId, CancellationToken cancellationToken = default);

    Task SaveAsync(Guid listingId, SheetReport report, CancellationToken cancellationToken = default);
}
