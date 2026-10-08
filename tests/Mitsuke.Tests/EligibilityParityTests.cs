using System.Globalization;
using System.Text.Json.Nodes;
using Kensaya.Worker.Core.Rules;

namespace Mitsuke.Tests;

/// <summary>
/// Mitsuke and Kensa-ya must give the same import-eligibility answer for the same car, word for word: the reasons
/// are shown to buyers. contract/eligibility-golden.json holds Kensa-ya's website answers (synced by
/// scripts/sync-kensaya-engine.sh) for AU, NZ and the US.
/// </summary>
public sealed class EligibilityParityTests
{
    private static readonly RulesCatalog Catalog = RulesCatalog.Load();
    private static readonly JsonObject Golden =
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contract", "eligibility-golden.json")))!.AsObject();
    private static readonly DateTimeOffset AsOf = DateTimeOffset.Parse((string)Golden["asOf"]!, CultureInfo.InvariantCulture);

    [Fact]
    public void Every_golden_case_matches_word_for_word()
    {
        var cases = Golden["eligibility"]!.AsArray();
        Assert.True(cases.Count >= 300, $"expected Kensa-ya's full case list, got {cases.Count}");
        foreach (var c in cases)
        {
            var code = (string)c!["country"]!;
            var actual = Eligibility.Evaluate(Catalog.GetRules(code)!, Input(c["input"]!));
            JsonAssert.Equal(c["output"], Varying(actual), $"{code} {c["input"]!.ToJsonString()}");
        }
    }

    private static EligibilityInput Input(JsonNode i) => new(
        Year: i["year"]?.GetValue<int>(),
        Month: i["month"]?.GetValue<int>(),
        AsOf: AsOf,
        YearBasis: (string?)i["yearBasis"] switch
        {
            "manufacture" => YearBasis.Manufacture,
            "first_registration" => YearBasis.FirstRegistration,
            "unclear" => YearBasis.Unclear,
            _ => null,
        },
        Modifications: i["modifications"]?.AsArray().Select(m => (string)m!).ToList());

    // The golden cases record only the fields that vary with input.
    private static JsonObject Varying(EligibilityResult r) => new()
    {
        ["verdict"] = r.Verdict,
        ["headline"] = r.Headline,
        ["reason"] = r.Reason,
        ["pathways"] = new JsonArray(r.Pathways.Select(p => (JsonNode)new JsonObject
        {
            ["id"] = p.Pathway.Id,
            ["verdict"] = p.Verdict,
            ["reason"] = p.Reason,
        }).ToArray()),
    };
}
