using Kensaya.Worker.Core.Rules;
using Mitsuke.Core;

namespace Mitsuke.Pricing;

/// <summary>
/// Import eligibility from Kensa-ya's engine and country rules (vendored, proven identical by EligibilityParityTests).
/// The feed's year is the production year; its month isn't kept, so the engine assumes December, the cautious choice
/// near a 25-year cut-off. Modifications from the decoded sheet can turn a "yes" into a "maybe".
/// </summary>
public sealed class KensayaEligibilityChecker(IRulesSource rules, TimeProvider clock) : IEligibilityChecker
{
    public async Task<ImportEligibility?> CheckAsync(
        Listing listing, string destination, IReadOnlyList<string>? modifications = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(destination);
        var countryRules = await rules.GetRulesAsync(destination.ToUpperInvariant(), cancellationToken);
        if (countryRules is null) return null;

        var result = Eligibility.Evaluate(countryRules, new EligibilityInput(
            listing.Year, AsOf: clock.GetUtcNow(), YearBasis: YearBasis.Manufacture, Modifications: modifications));

        return new ImportEligibility
        {
            Destination = destination.ToUpperInvariant(),
            Verdict = Verdict(result.Verdict),
            Headline = result.Headline,
            Reason = result.Reason,
            Pathways = result.Pathways.Select(p => new EligibilityPathway(p.Pathway.Name, Verdict(p.Verdict), p.Reason)).ToList(),
            Links = result.Links
                .Where(l => Uri.TryCreate(l.Url, UriKind.Absolute, out _))
                .Select(l => new EligibilityLink(l.Label, new Uri(l.Url)))
                .ToList(),
            RulesCheckedOn = result.RulesCheckedOn,
        };
    }

    private static EligibilityVerdict Verdict(string verdict) => verdict switch
    {
        "yes" => EligibilityVerdict.Yes,
        "no" => EligibilityVerdict.No,
        _ => EligibilityVerdict.Maybe,
    };
}
