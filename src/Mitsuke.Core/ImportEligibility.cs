namespace Mitsuke.Core;

public enum EligibilityVerdict
{
    Yes,
    Maybe,
    No,
}

public sealed record EligibilityPathway(string Name, EligibilityVerdict Verdict, string Reason);

public sealed record EligibilityLink(string Label, Uri Url);

/// <summary>
/// Whether a car can be imported to the destination, and by which pathway (e.g. the 25-year rule or the SEVS
/// register in Australia). Kensa-ya's verdict, word for word: a "maybe" means a check the buyer must make, never a guess.
/// </summary>
public sealed record ImportEligibility
{
    public required string Destination { get; init; }
    public required EligibilityVerdict Verdict { get; init; }

    /// <summary>"Likely eligible via Older vehicle (25-year rule)", "Possible via SEVS register", ...</summary>
    public required string Headline { get; init; }

    public required string Reason { get; init; }
    public IReadOnlyList<EligibilityPathway> Pathways { get; init; } = [];
    public IReadOnlyList<EligibilityLink> Links { get; init; } = [];

    /// <summary>When the country's rules were last checked against their sources (yyyy-MM-dd).</summary>
    public required string RulesCheckedOn { get; init; }
}

public interface IEligibilityChecker
{
    /// <summary>Null when there are no rules for the destination.</summary>
    Task<ImportEligibility?> CheckAsync(
        Listing listing, string destination, IReadOnlyList<string>? modifications = null, CancellationToken cancellationToken = default);
}
