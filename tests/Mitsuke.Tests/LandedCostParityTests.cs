using System.Text.Json;
using System.Text.Json.Nodes;
using Kensaya.Worker.Core.Rules;

namespace Mitsuke.Tests;

/// <summary>
/// Mitsuke and Kensa-ya must quote the same landed cost for the same car. contract/landed-golden.json holds
/// answers computed by Kensa-ya's website engine (synced by scripts/sync-kensaya-engine.sh); every one must
/// match to the cent. If this fails after a sync, the vendored engine and the data drifted apart.
/// </summary>
public sealed class LandedCostParityTests
{
    private static readonly RulesCatalog Catalog = RulesCatalog.Load();
    private static readonly JsonObject Golden =
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contract", "landed-golden.json")))!.AsObject();

    public static TheoryData<string> Countries => ["AU", "NZ", "US"];

    [Theory]
    [MemberData(nameof(Countries))]
    public void Full_result_matches_kensaya_field_for_field(string code)
    {
        var c = Golden["countries"]![code]!["landedFull"]!;
        var actual = LandedCost.Compute(Catalog.GetRules(code)!, Input(c["input"]!));
        JsonAssert.Equal(c["output"], JsonSerializer.SerializeToNode(actual, RulesJson.Options), $"{code} landed cost");
    }

    [Fact]
    public void Every_golden_case_matches()
    {
        var cases = Golden["landed"]!.AsArray();
        Assert.True(cases.Count >= 100, $"expected Kensa-ya's full case list, got {cases.Count}");
        foreach (var c in cases)
        {
            var code = (string)c!["country"]!;
            var actual = LandedCost.Compute(Catalog.GetRules(code)!, Input(c["input"]!));
            JsonAssert.Equal(c["output"], DropNulls(Varying(actual)), $"{code} {c["input"]!.ToJsonString()}");
        }
    }

    private static LandedCostInput Input(JsonNode i) => new(
        PriceJpy: i["priceJpy"]!.GetValue<double>(),
        JpyPerUnit: i["jpyPerUnit"]?.GetValue<double>(),
        UseAltThreshold: i["useAltThreshold"]?.GetValue<bool>() ?? false,
        Overrides: i["overrides"]?.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<double>()),
        UseAltRate: i["useAltRate"]?.GetValue<bool>() ?? false);

    // The golden cases record only the fields that vary with input.
    private static JsonObject Varying(LandedCostResult r) => new()
    {
        ["jpyPerUnit"] = r.JpyPerUnit,
        ["fxDate"] = r.FxDate,
        ["fxLive"] = r.FxLive,
        ["priceLocal"] = r.PriceLocal,
        ["total"] = r.Total,
        ["totalLow"] = r.TotalLow,
        ["totalHigh"] = r.TotalHigh,
        ["lines"] = new JsonArray(r.Lines.Select(l => (JsonNode)new JsonObject
        {
            ["id"] = l.Id,
            ["amount"] = l.Amount,
            ["low"] = l.Low,
            ["high"] = l.High,
            ["isEstimate"] = l.IsEstimate,
            ["isZero"] = l.IsZero,
            ["overridden"] = l.Overridden,
        }).ToArray()),
    };

    // JSON.stringify drops undefined; mirror that so missing and null compare equal.
    private static JsonNode? DropNulls(JsonNode? n)
    {
        switch (n)
        {
            case JsonObject o:
                foreach (var key in o.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList()) o.Remove(key);
                foreach (var (_, v) in o) DropNulls(v);
                break;
            case JsonArray a:
                foreach (var v in a) DropNulls(v);
                break;
        }
        return n;
    }
}
