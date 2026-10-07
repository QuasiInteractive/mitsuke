using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mitsuke.Tests;

/// <summary>Deep JSON comparison that reports every difference with its path.</summary>
internal static class JsonAssert
{
    public static void Equal(JsonNode? expected, JsonNode? actual, string context)
    {
        var diffs = new List<string>();
        Compare(expected, actual, "$", diffs);
        if (diffs.Count > 0)
            Assert.Fail($"{context}: {diffs.Count} difference(s)\n  " + string.Join("\n  ", diffs.Take(15)));
    }

    private static void Compare(JsonNode? e, JsonNode? a, string path, List<string> diffs)
    {
        switch (e)
        {
            case null when a is null:
                return;
            case null:
            case not null when a is null:
                diffs.Add($"{path}: expected {Show(e)}, got {Show(a)}");
                return;
            case JsonObject eo when a is JsonObject ao:
                foreach (var (key, ev) in eo)
                    if (!ao.ContainsKey(key)) diffs.Add($"{path}.{key}: missing (expected {Show(ev)})");
                    else Compare(ev, ao[key], $"{path}.{key}", diffs);
                foreach (var (key, av) in ao)
                    if (!eo.ContainsKey(key)) diffs.Add($"{path}.{key}: unexpected {Show(av)}");
                return;
            case JsonArray ea when a is JsonArray aa:
                if (ea.Count != aa.Count) diffs.Add($"{path}: expected {ea.Count} items, got {aa.Count}");
                for (var i = 0; i < Math.Min(ea.Count, aa.Count); i++) Compare(ea[i], aa[i], $"{path}[{i}]", diffs);
                return;
            case JsonValue ev when a is JsonValue av:
                var ek = ev.GetValueKind();
                var ak = av.GetValueKind();
                if (ek == JsonValueKind.Number && ak == JsonValueKind.Number)
                {
                    // Exact: both sides do the same IEEE-754 arithmetic.
                    if (ev.GetValue<double>() != av.GetValue<double>()) diffs.Add($"{path}: expected {ev}, got {av}");
                }
                else if (ev.ToJsonString() != av.ToJsonString())
                {
                    diffs.Add($"{path}: expected {Show(ev)}, got {Show(av)}");
                }
                return;
            default:
                diffs.Add($"{path}: expected {Show(e)}, got {Show(a)}");
                return;
        }
    }

    private static string Show(JsonNode? n)
    {
        var s = n?.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "undefined";
        return s.Length > 160 ? s[..160] + "…" : s;
    }
}
