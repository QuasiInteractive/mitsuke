using Kensaya.Worker.Core.Rules;
using Mitsuke.Core;

namespace Mitsuke.Pricing;

/// <summary>
/// Adapts Kensa-ya's landed-cost engine (vendored, see scripts/sync-kensaya-engine.sh) to Mitsuke's model.
/// The rules source supplies a live exchange rate when available and the rules file's rate otherwise.
/// </summary>
public sealed class KensayaLandedCostEstimator(IRulesSource rules) : ILandedCostEstimator
{
    public async Task<LandedEstimate?> EstimateAsync(Money price, string destination, CancellationToken cancellationToken = default)
    {
        if (price.Currency != "JPY") return null;
        var countryRules = await rules.GetRulesAsync(destination.ToUpperInvariant(), cancellationToken);
        if (countryRules is null) return null;

        // The engine works in double with JavaScript rounding so it matches the website to the cent;
        // its outputs are already rounded to cents, so converting to decimal here is exact enough.
        var result = LandedCost.Compute(countryRules, new LandedCostInput((double)price.Amount));
        var currency = result.Currency;
        Money M(double amount) => new((decimal)amount, currency);

        return new LandedEstimate
        {
            Destination = result.CountryCode,
            Total = M(result.Total),
            Low = M(result.TotalLow),
            High = M(result.TotalHigh),
            JpyPerUnit = (decimal)result.JpyPerUnit,
            FxDate = result.FxDate,
            FxLive = result.FxLive,
            Lines = result.Lines
                .Select(l => new Mitsuke.Core.CostLine(l.Id, l.Label, M(l.Amount), l.Low is { } lo ? M(lo) : null, l.High is { } hi ? M(hi) : null, l.IsEstimate, l.Note))
                .ToList(),
            Note = result.TotalsNote,
        };
    }
}
