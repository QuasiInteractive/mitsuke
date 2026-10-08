using System.Globalization;
using System.Text.Json.Serialization;

namespace Kensaya.Worker.Core.Rules;

// Port of src/lib/eligibility.ts. The wording is part of the contract: reasons
// are shown to users, so the tests compare them character for character with
// the TypeScript engine's.

public sealed record EligibilityInput(
    int? Year,
    int? Month = null,
    DateTimeOffset? AsOf = null,
    YearBasis? YearBasis = null,
    IReadOnlyList<string>? Modifications = null);

public sealed record PathwayResult(
    [property: JsonPropertyName("pathway")] Pathway Pathway,
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record HardBlockSummary(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("source_url")] string SourceUrl);

public sealed record EligibilityResult
{
    /// <summary>"yes", "no" or "maybe".</summary>
    [JsonPropertyName("verdict")] public required string Verdict { get; init; }
    [JsonPropertyName("headline")] public required string Headline { get; init; }
    [JsonPropertyName("reason")] public required string Reason { get; init; }
    [JsonPropertyName("pathways")] public required IReadOnlyList<PathwayResult> Pathways { get; init; }
    [JsonPropertyName("hardBlocks")] public required IReadOnlyList<HardBlockSummary> HardBlocks { get; init; }
    [JsonPropertyName("links")] public required IReadOnlyList<RuleLink> Links { get; init; }
    [JsonPropertyName("rulesCheckedOn")] public required string RulesCheckedOn { get; init; }
    [JsonPropertyName("intro")] public required string Intro { get; init; }
}

public static class Eligibility
{
    private const double MsPerYear = 365.25 * 24 * 3600 * 1000;

    /// <summary>Age in years, assuming December when the month is unknown (the conservative choice).</summary>
    private static double AgeInYears(int year, int? month, DateTimeOffset asOf)
    {
        var m = month is >= 1 and <= 12 ? month.Value : 12;
        var made = new DateTimeOffset(year, m, 1, 0, 0, 0, TimeSpan.Zero);
        return (asOf - made).TotalMilliseconds / MsPerYear;
    }

    private static string Dated(EligibilityInput i) => i.YearBasis switch
    {
        YearBasis.Manufacture => "Built",
        YearBasis.FirstRegistration => "First registered",
        _ => "Dated",
    };

    private static string N(double v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Modifications that match the country's trigger keywords.</summary>
    public static IReadOnlyList<string> FlaggedModifications(CountryRules rules, IReadOnlyList<string>? mods)
    {
        var caveat = rules.Eligibility.ModificationCaveat;
        if (caveat is null || mods is not { Count: > 0 }) return [];
        return mods.Where(m => caveat.TriggerKeywords.Any(k => m.ToLowerInvariant().Contains(k.ToLowerInvariant(), StringComparison.Ordinal))).ToList();
    }

    private static PathwayResult EvaluatePathway(Pathway p, EligibilityInput input, DateTimeOffset asOf)
    {
        switch (p.Criteria)
        {
            case MinAgeCriteria c:
            {
                if (input.Year is not { } year)
                    return new(p, "maybe", $"The year of manufacture was unclear on the sheet, so the {c.Years}-year rule could not be checked.");
                var age = AgeInYears(year, input.Month, asOf);
                if (age >= c.Years)
                {
                    var monthPart = input.Month is { } m and not 0 ? $"/{m:00}" : "";
                    return new(p, p.ResultIfMet, $"{Dated(input)} {year}{monthPart}, about {N(Math.Floor(age))} years old, which meets the {c.Years}-year threshold.");
                }
                var yearsToGo = (int)Math.Ceiling(c.Years - age);
                return new(p, "no", $"{Dated(input)} {year}, about {N(Math.Floor(age))} years old. It needs to be {c.Years}. Roughly {yearsToGo} more year{(yearsToGo == 1 ? "" : "s")} (month of manufacture matters, so check the build plate).");
            }
            case RegisterCriteria c:
                return new(p, "maybe", $"Check whether this exact make, model and variant is listed on the {c.RegisterName}. Only listed vehicles qualify under this pathway.");
            case StandardsCriteria c:
                return new(p, "maybe", $"The vehicle must meet: {string.Join("; ", c.Standards)}. A compliance workshop or entry certifier confirms this against the chassis code and build date.");
            case ManualCriteria c:
                return new(p, "maybe", c.Instructions);
            default:
                throw new InvalidDataException($"Pathway '{p.Id}' has an unknown criteria type.");
        }
    }

    /// <summary>
    /// Runs every pathway in the country's rules and picks the best verdict.
    /// One clear "yes" wins; otherwise any "maybe" gives maybe; all "no" gives no.
    /// </summary>
    public static EligibilityResult Evaluate(CountryRules rules, EligibilityInput input)
    {
        var asOf = input.AsOf ?? DateTimeOffset.UtcNow;
        var caveat = rules.Eligibility.ModificationCaveat;
        var flagged = FlaggedModifications(rules, input.Modifications);

        var pathways = rules.Eligibility.Pathways.Select(p =>
        {
            var r = EvaluatePathway(p, input, asOf);
            if (caveat is not null && flagged.Count > 0 && r.Verdict == "yes" && caveat.AffectsPathways.Contains(p.Id))
                return r with { Verdict = "maybe", Reason = $"{r.Reason} But the sheet lists modifications ({string.Join("; ", flagged)}). {caveat.Summary}" };
            return r;
        }).ToList();

        var yes = pathways.FirstOrDefault(p => p.Verdict == "yes");
        var maybe = pathways.Where(p => p.Verdict == "maybe").ToList();

        string verdict, headline, reason;
        if (yes is not null)
        {
            verdict = "yes";
            headline = $"Likely eligible via {yes.Pathway.Name}";
            reason = yes.Reason;
        }
        else if (maybe.Count > 0)
        {
            verdict = "maybe";
            // Lead with the pathway the age rule would otherwise have passed.
            var modded = flagged.Count > 0 ? maybe.FirstOrDefault(m => caveat!.AffectsPathways.Contains(m.Pathway.Id)) : null;
            // "Manual" pathways (migrants, returning residents) only apply to a few
            // people, so lead with the routes a normal auction buyer can use.
            var general = maybe.Where(m => m.Pathway.Criteria is not ManualCriteria).ToList();
            var pool = general.Count > 0 ? general : maybe;
            var lead = modded ?? pool[0];
            // Only special-case routes left (every general route said no): say so plainly.
            var onlySpecial = modded is null && general.Count == 0 && pathways.Any(p => p.Verdict == "no");
            headline = modded is not null
                ? $"Probably, via {lead.Pathway.Name}, {caveat!.HeadlineCondition}"
                : onlySpecial
                    ? "Unlikely: only through special exemptions"
                    : pool.Count == 1
                        ? $"Possible via {lead.Pathway.Name}"
                        : $"Possible, depends on {pool.Count} checks";
            reason = modded is not null
                ? lead.Reason
                : onlySpecial
                    ? string.Join(" ", pathways.Where(p => p.Verdict == "no").Concat(pool).Select(m => $"{m.Pathway.Name}: {m.Reason}"))
                    : string.Join(" ", pool.Select(m => $"{m.Pathway.Name}: {m.Reason}"));
        }
        else
        {
            verdict = "no";
            headline = "Not eligible on any standard pathway right now";
            reason = string.Join(" ", pathways.Select(m => $"{m.Pathway.Name}: {m.Reason}"));
        }

        return new EligibilityResult
        {
            Verdict = verdict,
            Headline = headline,
            Reason = reason,
            Pathways = pathways,
            HardBlocks = rules.Eligibility.HardBlocks.Select(b => new HardBlockSummary(b.Id, b.Summary, b.SourceUrl)).ToList(),
            Links = rules.Eligibility.Links,
            RulesCheckedOn = rules.RulesCheckedOn,
            Intro = rules.Eligibility.Intro,
        };
    }
}
