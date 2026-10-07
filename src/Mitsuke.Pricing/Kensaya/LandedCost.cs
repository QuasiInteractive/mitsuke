using System.Text.Json.Serialization;

namespace Kensaya.Worker.Core.Rules;

// Port of src/lib/landed-cost.ts. Uses double and JavaScript's rounding on
// purpose: the stored result must match the website's to the cent, and the
// tests check that against answers exported from the TypeScript engine.

public sealed record LandedCostInput(
    double PriceJpy,
    double? JpyPerUnit = null,
    bool UseAltThreshold = false,
    IReadOnlyDictionary<string, double>? Overrides = null,
    bool UseAltRate = false);

public sealed record CostLineResult
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("label")] public required string Label { get; init; }
    [JsonPropertyName("help")] public required string Help { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("amount")] public required double Amount { get; init; }
    [JsonPropertyName("low")] public double? Low { get; init; }
    [JsonPropertyName("high")] public double? High { get; init; }
    [JsonPropertyName("rate")] public double? Rate { get; init; }
    [JsonPropertyName("isEstimate")] public required bool IsEstimate { get; init; }
    [JsonPropertyName("isZero")] public required bool IsZero { get; init; }
    [JsonPropertyName("source_url")] public string? SourceUrl { get; init; }
    [JsonPropertyName("checked_on")] public string? CheckedOn { get; init; }
    [JsonPropertyName("note")] public string? Note { get; init; }
    [JsonPropertyName("overridden")] public required bool Overridden { get; init; }
}

public sealed record LandedCostResult
{
    [JsonPropertyName("currency")] public required string Currency { get; init; }
    [JsonPropertyName("countryCode")] public required string CountryCode { get; init; }
    [JsonPropertyName("jpyPerUnit")] public required double JpyPerUnit { get; init; }
    [JsonPropertyName("fxDate")] public string? FxDate { get; init; }
    [JsonPropertyName("fxLive")] public required bool FxLive { get; init; }
    [JsonPropertyName("priceLocal")] public required double PriceLocal { get; init; }
    [JsonPropertyName("lines")] public required IReadOnlyList<CostLineResult> Lines { get; init; }
    [JsonPropertyName("total")] public required double Total { get; init; }
    [JsonPropertyName("totalLow")] public required double TotalLow { get; init; }
    [JsonPropertyName("totalHigh")] public required double TotalHigh { get; init; }
    [JsonPropertyName("rulesCheckedOn")] public required string RulesCheckedOn { get; init; }
    [JsonPropertyName("totalsNote")] public required string TotalsNote { get; init; }
}

public static class LandedCost
{
    // Whole words only: "Truck" and "Pick-up" count, "Trucker" doesn't. Same as looksLikeTruck in landed-cost.ts.
    private static readonly System.Text.RegularExpressions.Regex TruckWords =
        new(@"\b(truck|pick-?up|trk)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Pickups and kei trucks are "goods vehicles" for US duty. Read from the sheet's model and trim.</summary>
    public static bool LooksLikeTruck(string? model, string? trim = null) => TruckWords.IsMatch($"{model ?? ""} {trim ?? ""}");

    /// <summary>JavaScript's Math.round(n * 100) / 100: halves round up, unlike .NET's default banker's rounding.</summary>
    public static double Round2(double n) => JsRound(n * 100) / 100;

    /// <summary>
    /// Math.round from JavaScript: nearest integer, ties toward +infinity. Not floor(x + 0.5),
    /// which is wrong for 0.49999999999999994 because the addition itself rounds.
    /// </summary>
    public static double JsRound(double x)
    {
        var r = Math.Floor(x);
        return x - r >= 0.5 ? r + 1 : r;
    }

    /// <summary>
    /// Walks the country's cost lines in order. Each percentage line sums the
    /// already-computed amounts named in its basis, so the data file decides how
    /// GST, duty and luxury taxes stack.
    /// </summary>
    public static LandedCostResult Compute(CountryRules rules, LandedCostInput input)
    {
        var userRate = input.JpyPerUnit is > 0;
        var jpyPerUnit = userRate ? input.JpyPerUnit!.Value : rules.Fx.JpyPerUnit;
        var priceLocal = Round2(input.PriceJpy / jpyPerUnit);

        // Insertion-ordered like the JS Maps, so totals add up in the same order (floating point).
        var amounts = new List<KeyValuePair<string, double>>();
        var lows = new List<KeyValuePair<string, double>>();
        var highs = new List<KeyValuePair<string, double>>();
        var lines = new List<CostLineResult>();

        static void Set(List<KeyValuePair<string, double>> m, string id, double v)
        {
            var i = m.FindIndex(kv => kv.Key == id);
            if (i >= 0) m[i] = new(id, v); else m.Add(new(id, v));
        }
        static double Sum(IReadOnlyList<string> ids, List<KeyValuePair<string, double>> m)
        {
            var acc = 0.0;
            foreach (var id in ids)
            {
                var i = m.FindIndex(kv => kv.Key == id);
                acc += i >= 0 ? m[i].Value : 0;
            }
            return acc;
        }

        foreach (var line in rules.CostLines)
        {
            double amount = 0;
            double? low = null, high = null, rate = null;
            var isEstimate = true;
            var overridden = false;
            double? overrideValue = input.Overrides is not null && input.Overrides.TryGetValue(line.Id, out var o) ? o : null;

            switch (line)
            {
                case PriceLine:
                    amount = priceLocal;
                    isEstimate = false;
                    break;
                case FixedLine f:
                    amount = f.Amount;
                    isEstimate = false;
                    break;
                case RangeLine r:
                    if (overrideValue is >= 0)
                    {
                        amount = overrideValue.Value;
                        overridden = true;
                        isEstimate = false;
                    }
                    else
                    {
                        low = r.Low;
                        high = r.High;
                        amount = (r.Low + r.High) / 2;
                    }
                    break;
                case PercentLine p:
                {
                    var r = input.UseAltRate && p.RateAlt is { } alt ? alt : p.Rate;
                    rate = r;
                    double Clamp(double n) => Math.Min(p.Max ?? double.PositiveInfinity, Math.Max(p.Min ?? 0, n));
                    amount = Clamp(Sum(p.Basis, amounts) * r / 100);
                    low = Clamp(Sum(p.Basis, lows) * r / 100);
                    high = Clamp(Sum(p.Basis, highs) * r / 100);
                    break;
                }
                case ThresholdPercentLine t:
                {
                    rate = t.Rate;
                    var threshold = input.UseAltThreshold && t.ThresholdAlt is > 0 ? t.ThresholdAlt.Value : t.Threshold;
                    double Calc(List<KeyValuePair<string, double>> m) =>
                        Math.Max(0, Sum(t.Basis, m) - threshold) * t.ExcessFactor * t.Rate / 100;
                    amount = Calc(amounts);
                    low = Calc(lows);
                    high = Calc(highs);
                    break;
                }
            }

            amount = Round2(amount);
            Set(amounts, line.Id, amount);
            Set(lows, line.Id, Round2(low ?? amount));
            Set(highs, line.Id, Round2(high ?? amount));

            lines.Add(new CostLineResult
            {
                Id = line.Id,
                Label = line.Label,
                Help = line.Help,
                Type = line.Kind,
                Amount = amount,
                Low = low is { } l ? Round2(l) : null,
                High = high is { } h ? Round2(h) : null,
                Rate = rate,
                IsEstimate = isEstimate,
                IsZero = amount == 0,
                SourceUrl = line.SourceUrl,
                CheckedOn = line.CheckedOn,
                Note = line.Note,
                Overridden = overridden,
            });
        }

        static double Total(List<KeyValuePair<string, double>> m) => Round2(m.Aggregate(0.0, (a, kv) => a + kv.Value));

        return new LandedCostResult
        {
            Currency = rules.Currency,
            CountryCode = rules.Code,
            JpyPerUnit = jpyPerUnit,
            FxDate = userRate ? null : rules.Fx.CheckedOn,
            FxLive = !userRate && rules.Fx.Live,
            PriceLocal = priceLocal,
            Lines = lines,
            Total = Total(amounts),
            TotalLow = Total(lows),
            TotalHigh = Total(highs),
            RulesCheckedOn = rules.RulesCheckedOn,
            TotalsNote = rules.TotalsNote,
        };
    }
}
