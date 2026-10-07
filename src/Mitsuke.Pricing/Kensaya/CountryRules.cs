using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kensaya.Worker.Core.Rules;

// C# mirror of src/lib/rules/schema.ts. Defaults match the zod defaults, because
// the eligibility result embeds these objects and must serialise exactly like the
// website's. Rates live in src/data/countries/*.json, never here.

public static class RulesJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectNullableAnnotations = true,
    };
}

public sealed class CountryEntry
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string Currency { get; init; }
    /// <summary>"full" or "translation_only".</summary>
    public required string Support { get; init; }
    public string? RulesFile { get; init; }
    public bool ComingSoon { get; init; }

    public bool IsFull => Support == "full";
}

public sealed class CountryIndex
{
    public required IReadOnlyList<CountryEntry> Countries { get; init; }
}

public sealed record Fx
{
    public required double JpyPerUnit { get; init; }
    public bool Live { get; init; }
    public string SourceUrl { get; init; } = "";
    public required string CheckedOn { get; init; }
    public string Note { get; init; } = "";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PriceLine), "price")]
[JsonDerivedType(typeof(FixedLine), "fixed")]
[JsonDerivedType(typeof(RangeLine), "range")]
[JsonDerivedType(typeof(PercentLine), "percent")]
[JsonDerivedType(typeof(ThresholdPercentLine), "threshold_percent")]
public abstract record CostLine
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string Help { get; init; } = "";
    // Sourced. On the price line zod's .partial() makes these optional, but in zod v4
    // the "" defaults still apply, so only checked_on can be missing.
    public string SourceUrl { get; init; } = "";
    public string? CheckedOn { get; init; }
    public string Note { get; init; } = "";

    [JsonIgnore] public abstract string Kind { get; }
}

/// <summary>The car itself, converted from yen.</summary>
public sealed record PriceLine : CostLine
{
    public override string Kind => "price";
}

public sealed record FixedLine : CostLine
{
    public required double Amount { get; init; }
    public override string Kind => "fixed";
}

/// <summary>A low/high estimate; totals use the midpoint.</summary>
public sealed record RangeLine : CostLine
{
    public required double Low { get; init; }
    public required double High { get; init; }
    public override string Kind => "range";
}

/// <summary>A percentage of the sum of earlier lines named in <see cref="Basis"/>, optionally clamped.</summary>
public sealed record PercentLine : CostLine
{
    public required double Rate { get; init; }
    public required IReadOnlyList<string> Basis { get; init; }
    /// <summary>Floor and cap on the amount (US merchandise processing fee).</summary>
    public double? Min { get; init; }
    public double? Max { get; init; }
    /// <summary>Rate used instead when the input asks for it (US: 25% duty on trucks).</summary>
    public double? RateAlt { get; init; }
    public string? RateAltLabel { get; init; }
    public override string Kind => "percent";
}

/// <summary>Luxury-car-tax style: the rate applies to the amount above a threshold.</summary>
public sealed record ThresholdPercentLine : CostLine
{
    public required double Rate { get; init; }
    public required IReadOnlyList<string> Basis { get; init; }
    public required double Threshold { get; init; }
    public double? ThresholdAlt { get; init; }
    public string? ThresholdAltLabel { get; init; }
    /// <summary>Multiplier on the excess before the rate (AU LCT uses 10/11 to strip GST).</summary>
    public double ExcessFactor { get; init; } = 1;
    public override string Kind => "threshold_percent";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MinAgeCriteria), "min_age_years")]
[JsonDerivedType(typeof(RegisterCriteria), "register")]
[JsonDerivedType(typeof(StandardsCriteria), "standards")]
[JsonDerivedType(typeof(ManualCriteria), "manual")]
public abstract record Criteria;

public sealed record MinAgeCriteria : Criteria
{
    public required int Years { get; init; }
    public string MeasuredFrom { get; init; } = "manufacture";
}

public sealed record RegisterCriteria : Criteria
{
    public required string RegisterName { get; init; }
    public required string RegisterUrl { get; init; }
}

public sealed record StandardsCriteria : Criteria
{
    public required IReadOnlyList<string> Standards { get; init; }
}

public sealed record ManualCriteria : Criteria
{
    public required string Instructions { get; init; }
}

public sealed record Pathway
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Summary { get; init; }
    public required Criteria Criteria { get; init; }
    /// <summary>"yes" or "maybe".</summary>
    public string ResultIfMet { get; init; } = "yes";
    public string? TypicalCostLine { get; init; }
    public string SourceUrl { get; init; } = "";
    public required string CheckedOn { get; init; }
    public string Note { get; init; } = "";
}

public sealed record HardBlock
{
    public required string Id { get; init; }
    public required string Summary { get; init; }
    public string SourceUrl { get; init; } = "";
    public required string CheckedOn { get; init; }
    public string Note { get; init; } = "";
}

public sealed record ModificationCaveat
{
    public required string Summary { get; init; }
    public required IReadOnlyList<string> TriggerKeywords { get; init; }
    public required IReadOnlyList<string> AffectsPathways { get; init; }
    /// <summary>Ends the headline "Probably, via &lt;pathway&gt;, &lt;condition&gt;".</summary>
    public string HeadlineCondition { get; init; } = "if the modifications are old enough";
}

public sealed record RuleLink(string Label, string Url);

public sealed record EligibilityRules
{
    public required string Intro { get; init; }
    public required IReadOnlyList<Pathway> Pathways { get; init; }
    public IReadOnlyList<HardBlock> HardBlocks { get; init; } = [];
    public ModificationCaveat? ModificationCaveat { get; init; }
    public IReadOnlyList<RuleLink> Links { get; init; } = [];
}

public sealed record RuleInputs
{
    public bool AskFuelEfficient { get; init; }
    public bool AskEngineCc { get; init; }
    /// <summary>The country distinguishes trucks (percent lines' rate_alt applies to them).</summary>
    public bool AskTruck { get; init; }
}

public sealed record CountryRules
{
    public RuleInputs Inputs { get; init; } = new();
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string Currency { get; init; }
    public string Locale { get; init; } = "en-AU";
    public required string RulesCheckedOn { get; init; }
    public required Fx Fx { get; init; }
    public required IReadOnlyList<CostLine> CostLines { get; init; }
    public string TotalsNote { get; init; } = "";
    public required EligibilityRules Eligibility { get; init; }
    public IReadOnlyList<string> Gotchas { get; init; } = [];
}
