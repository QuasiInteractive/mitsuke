using System.Globalization;

namespace Mitsuke.Core;

/// <summary>Which generation a car is, e.g. "Lancer Evolution VIII", and how sure we are.</summary>
/// <param name="Certain">False when it rests on the machine-read sheet or a build date that fits more than one.</param>
/// <param name="Reason">One sentence a buyer can check, e.g. "Built Mar 2006: the Evo IX years."</param>
public sealed record VariantGuess(string Name, bool Certain, string Reason);

/// <summary>
/// Tells generations apart when one chassis code spans several: CT9A is the Evo VII, VIII and IX. Uses the build date,
/// then the gearbox (a 6-speed manual rules out generations that only had a 5-speed). Production windows follow the
/// Japanese launch dates, so a car built in a changeover month can fit two; then the answer says so.
/// </summary>
public static class ModelVariants
{
    private sealed record Generation(string Make, string Code, string Name, int From, int To, int[] ManualGears);

    private static int Ym(int year, int month) => (year * 12) + month - 1;

    // Add a model here only with its code, launch month and gearboxes. Order matters only for display.
    private static readonly Generation[] Generations =
    [
        new("Mitsubishi", "CE9A", "Lancer Evolution I", Ym(1992, 10), Ym(1993, 12), [5]),
        new("Mitsubishi", "CE9A", "Lancer Evolution II", Ym(1994, 1), Ym(1994, 12), [5]),
        new("Mitsubishi", "CE9A", "Lancer Evolution III", Ym(1995, 1), Ym(1996, 7), [5]),
        new("Mitsubishi", "CN9A", "Lancer Evolution IV", Ym(1996, 8), Ym(1997, 12), [5]),
        new("Mitsubishi", "CP9A", "Lancer Evolution V", Ym(1998, 1), Ym(1998, 12), [5]),
        new("Mitsubishi", "CP9A", "Lancer Evolution VI", Ym(1999, 1), Ym(2001, 1), [5]),
        new("Mitsubishi", "CT9A", "Lancer Evolution VII", Ym(2001, 2), Ym(2003, 1), [5]),
        new("Mitsubishi", "CT9A", "Lancer Evolution VIII", Ym(2003, 1), Ym(2005, 2), [5, 6]),
        new("Mitsubishi", "CT9A", "Lancer Evolution IX", Ym(2005, 3), Ym(2007, 9), [5, 6]),
        new("Mitsubishi", "CT9W", "Lancer Evolution IX Wagon", Ym(2005, 9), Ym(2007, 9), [6]),
        new("Mitsubishi", "CZ4A", "Lancer Evolution X", Ym(2007, 10), Ym(2016, 4), [5]),
    ];

    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");

    public static VariantGuess? Identify(Listing listing, ListingDetails? details = null)
    {
        ArgumentNullException.ThrowIfNull(listing);
        if (listing.ModelCode is not { } code || listing.Year is not { } year) return null;

        var month = listing.Month;
        var byCode = Generations.Where(g => g.Make.Equals(listing.Make, StringComparison.OrdinalIgnoreCase)
                                            && g.Code.Equals(code, StringComparison.OrdinalIgnoreCase)).ToList();
        var byDate = byCode.Where(g => month is { } m
            ? Ym(year, m) >= g.From && Ym(year, m) <= g.To
            : year >= g.From / 12 && year <= g.To / 12).ToList();
        if (byDate.Count == 0) return null;

        var when = month is { } mm ? new DateTime(year, mm, 1).ToString("MMM yyyy", Au) : year.ToString(CultureInfo.InvariantCulture);
        if (byDate.Count == 1)
            return new VariantGuess(byDate[0].Name, month is not null, $"{code} built {when}: the {Short(byDate[0])} years.");

        // Several fit the date: a manual gearbox can rule some out.
        if (details?.ManualGears is { } gears)
        {
            var byGears = byDate.Where(g => g.ManualGears.Contains(gears)).ToList();
            var ruledOut = byDate.Except(byGears).ToList();
            if (byGears.Count == 1 && ruledOut.Count > 0)
                return new VariantGuess(byGears[0].Name, false,
                    $"Built {when}, when the {string.Join(" and ", byDate.Select(Short))} overlap. The auction sheet shows a {gears}-speed manual, "
                    + $"which rules out the {string.Join(" and ", ruledOut.Select(g => $"{Short(g)} ({string.Join("/", g.ManualGears)}-speed only)"))}.");
        }

        return new VariantGuess(string.Join(" or ", byDate.Select((g, i) => i == 0 ? g.Name : Short(g))), false,
            $"Built {when}, which fits the {string.Join(" and ", byDate.Select(Short))}. The auction sheet or the car's build plate will say which.");
    }

    /// <summary>"Evo VIII" from "Lancer Evolution VIII".</summary>
    private static string Short(Generation g) => g.Name.Replace("Lancer Evolution", "Evo", StringComparison.Ordinal);
}
