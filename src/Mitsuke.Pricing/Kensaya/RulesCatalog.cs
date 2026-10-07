using System.Text.Json;

namespace Kensaya.Worker.Core.Rules;

/// <summary>
/// The countries index and each country's rules, loaded once from the website's
/// data files. Adding a country is a data change: a new entry in countries.json
/// and a rules file, no code.
/// </summary>
public sealed class RulesCatalog
{
    private readonly Dictionary<string, CountryEntry> _countries;
    private readonly Dictionary<string, CountryRules> _rules;

    private RulesCatalog(Dictionary<string, CountryEntry> countries, Dictionary<string, CountryRules> rules)
    {
        _countries = countries;
        _rules = rules;
    }

    public static RulesCatalog Load(string? directory = null)
    {
        var dir = directory ?? Mitsuke.Pricing.PricingData.DefaultDirectory;
        var index = Read<CountryIndex>(Path.Combine(dir, "countries.json"));
        var countries = index.Countries.ToDictionary(c => c.Code, StringComparer.Ordinal);
        var rules = new Dictionary<string, CountryRules>(StringComparer.Ordinal);
        foreach (var c in index.Countries.Where(c => c.IsFull))
        {
            if (c.RulesFile is null) throw new InvalidDataException($"{c.Code} has full support but no rules_file.");
            var r = Read<CountryRules>(Path.Combine(dir, "countries", c.RulesFile + ".json"));
            Validate(r);
            rules[c.Code] = r;
        }
        return new RulesCatalog(countries, rules);
    }

    public CountryEntry? GetCountry(string code) => _countries.GetValueOrDefault(code);

    /// <summary>Rules for a fully supported country; null for translation-only or unknown countries.</summary>
    public CountryRules? GetRules(string code) => _rules.GetValueOrDefault(code);

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), RulesJson.Options)
        ?? throw new InvalidDataException($"{Path.GetFileName(path)} is empty.");

    /// <summary>The checks zod does on the website, so a bad rules file fails at startup, not mid-job.</summary>
    private static void Validate(CountryRules r)
    {
        void Require(bool ok, string what) { if (!ok) throw new InvalidDataException($"{r.Code} rules: {what}"); }
        Require(r.Code.Length == 2 && r.Currency.Length == 3, "code/currency length");
        Require(r.Fx.JpyPerUnit > 0, "fx.jpy_per_unit must be positive");
        Require(r.CostLines.Count > 0, "no cost lines");
        Require(r.Eligibility.Pathways.Count > 0, "no pathways");
        var ids = new HashSet<string>();
        foreach (var line in r.CostLines)
        {
            Require(ids.Add(line.Id), $"duplicate cost line id '{line.Id}'");
            if (line is not PriceLine) Require(line.CheckedOn is not null, $"cost line '{line.Id}' has no checked_on");
            if (line is PercentLine p) Require(p.Basis.Count > 0 && p.Rate >= 0, $"cost line '{line.Id}' basis/rate");
            if (line is ThresholdPercentLine t) Require(t.Basis.Count > 0 && t.Rate >= 0 && t.ExcessFactor > 0, $"cost line '{line.Id}' basis/rate/factor");
            if (line is RangeLine g) Require(g.Low >= 0 && g.High >= 0, $"cost line '{line.Id}' range");
        }
    }
}
