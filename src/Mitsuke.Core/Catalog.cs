using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Mitsuke.Core;

/// <summary>A make at auction and how many lots it has listed right now, e.g. ("Mitsubishi", "mitsubishi", 1697).</summary>
public sealed record CatalogMake(string Name, string Slug, int Lots);

/// <summary>
/// A model of one make. <paramref name="Name"/> is how listings spell it (so a watchlist built from it matches them);
/// <paramref name="Label"/> is how to show it ("RX-7", not "Rx-7").
/// </summary>
public sealed record CatalogModel(string Make, string Name, string Label, string Slug, int Lots);

/// <summary>
/// One generation of a model, identified by its chassis code: what the new-watchlist form offers after a model.
/// <paramref name="Label"/> is how enthusiasts call it ("R32 GT-R"); <paramref name="Seen"/> counts lots Mitsuke has
/// observed with that code (0 for curated generations not seen yet).
/// </summary>
public sealed record CatalogGeneration(string Code, string Label, int YearFrom, int YearTo, int Seen, bool Curated);

/// <summary>Makes and models as a source lists them. TheCarApi's adapter implements it.</summary>
public interface ICatalogSource
{
    Task<IReadOnlyList<CatalogMake>> GetMakesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogModel>> GetModelsAsync(CatalogMake make, CancellationToken cancellationToken = default);
}

public interface ICatalogStore
{
    /// <summary>Replaces the whole catalogue in one transaction, so readers never see half a sync.</summary>
    Task ReplaceAsync(IReadOnlyList<CatalogMake> makes, IReadOnlyList<CatalogModel> models, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogMake>> GetMakesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogModel>> GetModelsAsync(string make, CancellationToken cancellationToken = default);

    /// <summary>Chassis codes Mitsuke has actually seen for a make and model, with the production years they came with.</summary>
    Task<IReadOnlyList<CatalogGeneration>> GetObservedGenerationsAsync(string make, string model, CancellationToken cancellationToken = default);
}

/// <summary>
/// Refreshes the catalogue from the source: one request for the makes, one per make for its models (about 75 a day).
/// A make whose models fail keeps its previous models rather than vanishing from the form.
/// </summary>
public sealed partial class CatalogSync(ICatalogSource source, ICatalogStore store, ILogger<CatalogSync> logger)
{
    public async Task<(int Makes, int Models)> RunAsync(CancellationToken cancellationToken = default)
    {
        var makes = (await source.GetMakesAsync(cancellationToken)).Where(m => m.Lots > 0).ToList();
        var models = new List<CatalogModel>();
        foreach (var make in makes)
        {
            try
            {
                models.AddRange(await source.GetModelsAsync(make, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogMakeFailed(logger, ex, make.Name);
                models.AddRange(await store.GetModelsAsync(make.Name, cancellationToken));
            }
        }

        await store.ReplaceAsync(makes, models, cancellationToken);
        LogSynced(logger, makes.Count, models.Count);
        return (makes.Count, models.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalogue synced: {Makes} makes, {Models} models")]
    private static partial void LogSynced(ILogger logger, int makes, int models);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't fetch models for {Make}; keeping the previous list")]
    private static partial void LogMakeFailed(ILogger logger, Exception ex, string make);
}

/// <summary>
/// Generations enthusiasts search by, with their chassis codes and production years, for models where the code is
/// what tells them apart (an R32 GT-R is BNR32, an R32 GTS-t is HCR32). Model names are as the Japanese feed spells
/// them. Codes and years seen in real lots are merged in by <see cref="Merge"/>, so this list only needs the classics.
/// </summary>
public static class KnownGenerations
{
    private sealed record Entry(string Make, string Model, string Code, string Label, int From, int To);

    private static readonly Entry[] All =
    [
        new("Nissan", "Skyline", "BNR32", "R32 GT-R", 1989, 1994),
        new("Nissan", "Skyline", "HCR32", "R32 GTS-t", 1989, 1993),
        new("Nissan", "Skyline", "BCNR33", "R33 GT-R", 1995, 1998),
        new("Nissan", "Skyline", "ECR33", "R33 GTS25t", 1993, 1998),
        new("Nissan", "Skyline", "BNR34", "R34 GT-R", 1999, 2002),
        new("Nissan", "Skyline", "ER34", "R34 25GT-t", 1998, 2001),
        new("Nissan", "GT-R", "R35", "R35 GT-R", 2007, 2026),
        new("Nissan", "Silvia", "S13", "S13 Silvia", 1988, 1991),
        new("Nissan", "Silvia", "PS13", "S13 Silvia (SR20)", 1991, 1993),
        new("Nissan", "Silvia", "S14", "S14 Silvia", 1993, 1998),
        new("Nissan", "Silvia", "S15", "S15 Silvia", 1999, 2002),
        new("Nissan", "180SX", "RPS13", "180SX (SR20)", 1991, 1998),
        new("Nissan", "Fairlady Z", "Z33", "350Z (Z33)", 2002, 2008),
        new("Nissan", "Fairlady Z", "Z34", "370Z (Z34)", 2008, 2020),
        new("Toyota", "Supra", "JZA80", "A80 Supra", 1993, 2002),
        new("Toyota", "Supra", "JZA70", "A70 Supra (1JZ)", 1990, 1993),
        new("Toyota", "Chaser", "JZX100", "JZX100 Chaser", 1996, 2001),
        new("Toyota", "Chaser", "JZX90", "JZX90 Chaser", 1992, 1996),
        new("Toyota", "Mark Ii", "JZX100", "JZX100 Mark II", 1996, 2000),
        new("Toyota", "Mark Ii", "JZX90", "JZX90 Mark II", 1992, 1996),
        new("Toyota", "Mark Ii", "JZX110", "JZX110 Mark II", 2000, 2004),
        new("Toyota", "Sprinter Trueno", "AE86", "AE86 Trueno", 1983, 1987),
        new("Toyota", "Corolla Levin", "AE86", "AE86 Levin", 1983, 1987),
        new("Toyota", "Aristo", "JZS161", "JZS161 Aristo", 1997, 2004),
        new("Toyota", "Soarer", "JZZ30", "JZZ30 Soarer", 1991, 2000),
        new("Toyota", "86", "ZN6", "86 (ZN6)", 2012, 2021),
        new("Mazda", "Rx-7", "FD3S", "FD RX-7", 1991, 2002),
        new("Mazda", "Rx-7", "FC3S", "FC RX-7", 1985, 1992),
        new("Mazda", "Rx-8", "SE3P", "RX-8", 2003, 2012),
        new("Honda", "Nsx", "NA1", "NSX (NA1)", 1990, 2001),
        new("Honda", "Nsx", "NA2", "NSX (NA2)", 1997, 2005),
        new("Honda", "S2000", "AP1", "S2000 (AP1)", 1999, 2005),
        new("Honda", "S2000", "AP2", "S2000 (AP2)", 2005, 2009),
        new("Honda", "Civic", "EK9", "EK9 Civic Type R", 1997, 2000),
        new("Honda", "Civic", "EP3", "EP3 Civic Type R", 2001, 2005),
        new("Honda", "Civic", "FD2", "FD2 Civic Type R", 2007, 2010),
        new("Honda", "Civic", "FK8", "FK8 Civic Type R", 2017, 2021),
        new("Honda", "Integra", "DC2", "DC2 Integra", 1993, 2001),
        new("Honda", "Integra", "DC5", "DC5 Integra", 2001, 2006),
        new("Subaru", "Impreza", "GC8", "GC8 Impreza WRX", 1992, 2000),
        new("Subaru", "Impreza", "GDB", "GDB Impreza WRX STI", 2000, 2007),
        new("Subaru", "Impreza", "GRB", "GRB Impreza WRX STI", 2007, 2014),
        new("Mitsubishi", "Lancer Evolution", "CE9A", "Evo I–III", 1992, 1996),
        new("Mitsubishi", "Lancer Evolution", "CN9A", "Evo IV", 1996, 1998),
        new("Mitsubishi", "Lancer Evolution", "CP9A", "Evo V–VI", 1998, 2001),
        new("Mitsubishi", "Lancer Evolution", "CT9A", "Evo VII–IX", 2001, 2007),
        new("Mitsubishi", "Lancer Evolution", "CT9W", "Evo IX Wagon", 2005, 2007),
        new("Mitsubishi", "Lancer Evolution", "CZ4A", "Evo X", 2007, 2016),
        new("Mitsubishi", "Gto", "Z16A", "GTO (Z16A)", 1990, 2000),
        new("Bmw", "M3", "BL32", "E46 M3", 2000, 2006),
        new("Bmw", "3 Series", "AL19", "E46 318i", 1998, 2001),
        new("Bmw", "3 Series", "AY20", "E46 318i", 2001, 2005),
        new("Bmw", "3 Series", "AV22", "E46 320i", 2001, 2005),
        new("Bmw", "3 Series", "AV25", "E46 325i", 2001, 2005),
        new("Bmw", "3 Series", "AV30", "E46 330i", 2000, 2006),
    ];

    /// <summary>Curated generations for a model plus any codes seen in real lots, newest first. Seen codes the list doesn't name get the code as their label.</summary>
    public static IReadOnlyList<CatalogGeneration> Merge(string make, string model, IEnumerable<CatalogGeneration> observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        var seen = observed.Where(o => o.Seen > 0).ToDictionary(o => o.Code, StringComparer.OrdinalIgnoreCase);
        var curated = All.Where(e => Same(e.Make, make) && Same(e.Model, model))
            .Select(e => seen.TryGetValue(e.Code, out var s)
                ? new CatalogGeneration(e.Code, e.Label, e.From, e.To, s.Seen, true)
                : new CatalogGeneration(e.Code, e.Label, e.From, e.To, 0, true))
            .ToList();
        var extra = seen.Values.Where(s => !curated.Any(c => Same(c.Code, s.Code)));
        return curated.Concat(extra).OrderByDescending(g => g.YearTo).ThenByDescending(g => g.YearFrom).ThenBy(g => g.Code, StringComparer.Ordinal).ToList();
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>"LANCER EVOLUTION" as listings name it: "Lancer Evolution".</summary>
    public static string DisplayName(string raw) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase((raw ?? "").Trim().ToLowerInvariant());

    /// <summary>
    /// A feed name ("RX-7", "MARK II", "LANCER EVOLUTION") for display: short words, and words with digits or hyphens,
    /// stay upper case (RX-7, NSX, GT-R, II, S2000); the rest are title case (Lancer Evolution).
    /// </summary>
    public static string Label(string raw) => string.Join(' ', (raw ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(w => w.Length <= 3 || w.Any(char.IsDigit) || w.Contains('-', StringComparison.Ordinal)
            ? w.ToUpperInvariant()
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w.ToLowerInvariant())));
}
