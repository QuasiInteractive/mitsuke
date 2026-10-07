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
        // The brief's example: "R32 GT-R, under $45K AUD landed".
        Destination = "AU",
        MaxLanded = new Money(45_000m, "AUD"),
    };
}
