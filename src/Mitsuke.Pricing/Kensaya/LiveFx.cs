using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Kensaya.Worker.Core.Rules;

/// <summary>
/// Port of src/lib/fx.ts: live yen rates from the European Central Bank via
/// Frankfurter (free, no key), cached for 6 hours. If the service is down or
/// returns something implausible, the rules file's fallback rate is used.
/// </summary>
public sealed class LiveFx(HttpClient http, RulesCatalog catalog, TimeProvider time, ILogger<LiveFx> log) : IRulesSource
{
    public RulesCatalog Catalog => catalog;

    public const string SourceUrl = "https://www.frankfurter.dev/";
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, double Rate, string Date)> _cache = new();

    /// <summary>The country's rules with the live rate swapped in, or unchanged if unavailable. Null for translation-only countries.</summary>
    public async Task<CountryRules?> GetRulesAsync(string code, CancellationToken ct)
    {
        var rules = catalog.GetRules(code);
        if (rules is null) return null;
        var fx = await GetRateAsync(rules.Currency, ct);
        if (fx is not { } live) return rules;
        return rules with
        {
            Fx = rules.Fx with
            {
                JpyPerUnit = live.Rate,
                SourceUrl = SourceUrl,
                CheckedOn = live.Date,
                Note = $"European Central Bank reference rate for {live.Date}, via Frankfurter. Refreshed every few hours. Exporters and banks add a margin, so your actual rate will be a little worse.",
                Live = true,
            },
        };
    }

    private async Task<(double Rate, string Date)?> GetRateAsync(string currency, CancellationToken ct)
    {
        if (_cache.TryGetValue(currency, out var hit) && time.GetUtcNow() - hit.At < CacheFor) return (hit.Rate, hit.Date);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            var url = $"https://api.frankfurter.dev/v1/latest?base={Uri.EscapeDataString(currency)}&symbols=JPY";
            using var res = await http.GetAsync(url, cts.Token);
            if (!res.IsSuccessStatusCode) return Fallback(currency, $"HTTP {(int)res.StatusCode}");
            var body = JsonNode.Parse(await res.Content.ReadAsStringAsync(cts.Token));
            var rate = body?["rates"]?["JPY"]?.GetValue<double>();
            var date = (string?)body?["date"];
            // Yen per AUD/NZD/USD/EUR/GBP/CAD sits well inside this band.
            if (rate is not (> 20 and < 1000) || date is null) return Fallback(currency, "implausible response");
            var rounded = LandedCost.Round2(rate.Value);
            _cache[currency] = (time.GetUtcNow(), rounded, date);
            return (rounded, date);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException && !ct.IsCancellationRequested)
        {
            return Fallback(currency, ex.GetType().Name);
        }
    }

    private (double, string)? Fallback(string currency, string why)
    {
        log.LogWarning("Live FX for {Currency} unavailable ({Why}); using the rules file rate", currency, why);
        return null;
    }
}
