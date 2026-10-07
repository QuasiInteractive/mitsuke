using Mitsuke.Core;

namespace Mitsuke.Cli;

internal static class DemoWatchlist
{
    public static readonly Watchlist R32Gtr = new()
    {
        Id = Guid.Parse("7c3f2a10-0000-4000-8000-000000000032"),
        Name = "R32 GT-R",
        Make = "Nissan",
        Model = "Skyline",
        ModelCodes = new HashSet<string> { "BNR32" },
        YearFrom = 1989,
        YearTo = 1994,
        MaxMileageKm = 150_000,
        MinGrade = 3.5m,
        // ~A$45K landed works back to roughly ¥4.5M at auction; replaced by a real landed-cost limit later.
        MaxPrice = new Money(4_500_000m, "JPY"),
    };
}
