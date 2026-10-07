using System.Globalization;

namespace Mitsuke.Core;

/// <summary>Another car's price, used to judge whether a listing is good value.</summary>
public sealed record Comparable(ListingKey Key, int? Year, int? MileageKm, decimal? GradeScore, bool IsRepaired, Money Price);

public enum ScoreConfidence
{
    /// <summary>Too few comparable cars to score at all.</summary>
    None,
    Low,
    Normal,
}

/// <summary>
/// How a listing's price sits against comparable cars: 100 means cheaper than all of them, 0 dearer than all.
/// It compares <em>opening bids</em> with opening bids (there are no Japanese sale prices), so it says
/// "cheap for its spec", never "guaranteed bargain".
/// </summary>
public sealed record DealScore
{
    public int? Score { get; init; }
    public required string Label { get; init; }
    public required ScoreConfidence Confidence { get; init; }
    public required int ComparableCount { get; init; }

    /// <summary>Median price of the comparables.</summary>
    public Money? Typical { get; init; }

    /// <summary>Typical minus this price: positive means cheaper than typical.</summary>
    public Money? BelowTypical { get; init; }

    /// <summary>Plain-English description of what it was compared with.</summary>
    public required string Basis { get; init; }
}

public static class DealScorer
{
    public const int MinimumComparables = 5;

    // Below this many tight matches we widen to the broad set and say the score is low-confidence.
    private const int TightEnough = 8;
    private const int NormalConfidence = 15;

    /// <param name="subject">The listing being scored.</param>
    /// <param name="candidates">Other cars of the same model code and roughly the same age, one per physical car,
    /// never including the subject itself. The scorer narrows them further.</param>
    public static DealScore Score(Listing subject, IReadOnlyList<Comparable> candidates)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(candidates);
        var name = subject.ModelCode ?? subject.Model;

        if (subject.Price is not { } price)
            return NoScore("No price to compare", 0, name);

        var sameCurrency = candidates.Where(c => c.Price.Currency == price.Currency && c.Price.Amount > 0).ToList();
        var tight = sameCurrency.Where(c => IsTightMatch(subject, c)).ToList();
        var useTight = tight.Count >= TightEnough;
        var comps = useTight ? tight : sameCurrency;

        if (comps.Count < MinimumComparables)
            return NoScore($"Not enough similar cars yet ({comps.Count})", comps.Count, name);

        // Share of comparables that are cheaper; ties count half so equal prices land in the middle.
        var cheaper = comps.Count(c => c.Price.Amount < price.Amount) + 0.5m * comps.Count(c => c.Price.Amount == price.Amount);
        var score = (int)Math.Round(100m * (1m - cheaper / comps.Count), MidpointRounding.AwayFromZero);

        var typical = Median(comps.Select(c => c.Price.Amount));
        var years = comps.Where(c => c.Year is not null).Select(c => c.Year!.Value).ToList();
        var era = years.Count == 0 ? "" : years.Min() == years.Max()
            ? string.Create(CultureInfo.InvariantCulture, $"{years.Min()}, ")
            : string.Create(CultureInfo.InvariantCulture, $"{years.Min()}–{years.Max()}, ");
        var basis = useTight
            ? string.Create(CultureInfo.InvariantCulture, $"{comps.Count} similar {name}s, {era}similar mileage and grade")
            : string.Create(CultureInfo.InvariantCulture, $"{comps.Count} {name}s, {era}any mileage and grade");

        return new DealScore
        {
            Score = score,
            Label = LabelFor(score),
            Confidence = useTight && comps.Count >= NormalConfidence ? ScoreConfidence.Normal : ScoreConfidence.Low,
            ComparableCount = comps.Count,
            Typical = new Money(typical, price.Currency),
            BelowTypical = new Money(typical - price.Amount, price.Currency),
            Basis = basis,
        };
    }

    public static string LabelFor(int score) => score switch
    {
        >= 80 => "Great value",
        >= 60 => "Good value",
        >= 40 => "Typical price",
        _ => "Above typical",
    };

    /// <summary>Same era, similar mileage, similar condition. Unknowns on either side don't exclude a car.</summary>
    internal static bool IsTightMatch(Listing subject, Comparable c)
    {
        if (subject.Year is { } y && c.Year is { } cy && Math.Abs(y - cy) > 1) return false;

        if (subject.MileageKm is { } km && c.MileageKm is { } ckm)
        {
            var tolerance = Math.Max(km * 0.3, 20_000);
            if (Math.Abs(km - ckm) > tolerance) return false;
        }

        var repaired = subject.Grade?.IsRepaired ?? false;
        if (repaired != c.IsRepaired) return false;
        if (subject.Grade?.Score is { } g && c.GradeScore is { } cg && Math.Abs(g - cg) > 0.5m) return false;

        return true;
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static DealScore NoScore(string label, int count, string name) => new()
    {
        Score = null,
        Label = label,
        Confidence = ScoreConfidence.None,
        ComparableCount = count,
        Basis = $"{count} comparable {name}s",
    };
}

public interface IComparablesStore
{
    /// <summary>
    /// The latest price of every other car with the same make, model and model code built within
    /// <paramref name="yearWindow"/> years, one row per physical car (relists collapsed), excluding the subject car.
    /// </summary>
    Task<IReadOnlyList<Comparable>> GetCandidatesAsync(Listing subject, int yearWindow = 3, CancellationToken cancellationToken = default);
}
