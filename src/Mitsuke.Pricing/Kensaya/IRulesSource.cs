namespace Kensaya.Worker.Core.Rules;

/// <summary>Country rules as used for one job (with the live exchange rate in production).</summary>
public interface IRulesSource
{
    RulesCatalog Catalog { get; }

    /// <summary>Null for translation-only or unknown countries.</summary>
    Task<CountryRules?> GetRulesAsync(string code, CancellationToken ct);
}

/// <summary>Rules exactly as in the data files (fallback exchange rate). For tests and offline runs.</summary>
public sealed class FileRulesSource(RulesCatalog catalog) : IRulesSource
{
    public RulesCatalog Catalog => catalog;
    public Task<CountryRules?> GetRulesAsync(string code, CancellationToken ct) => Task.FromResult(catalog.GetRules(code));
}
