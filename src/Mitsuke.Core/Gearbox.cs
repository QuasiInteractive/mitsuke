namespace Mitsuke.Core;

/// <summary>
/// Manual or automatic, which moves the price of the same car a lot (an automatic NA1 NSX opens well under a manual
/// one). The feed's gearbox field is "Manual" or "Automatic" and missing on most search rows, so unknown stays unknown.
/// </summary>
public static class Gearbox
{
    public const string Manual = "manual";
    public const string Automatic = "automatic";

    /// <summary>"manual", "automatic" (including CVT and semi-automatics) or null when it isn't known.</summary>
    public static string? Class(string? transmission)
    {
        if (string.IsNullOrWhiteSpace(transmission)) return null;
        var t = transmission.Trim().ToLowerInvariant();
        if (t.Contains("manual", StringComparison.Ordinal) || t is "mt" or "m/t" || (t.Length == 2 && t[0] is 'f' or 'c' && char.IsDigit(t[1])))
            return Manual;
        if (t.Contains("auto", StringComparison.Ordinal) || t.Contains("cvt", StringComparison.Ordinal) || t.Contains("semi", StringComparison.Ordinal)
            || t is "at" or "a/t")
            return Automatic;
        return null;
    }

    /// <summary>Different only when both are known and differ: an unknown gearbox never excludes a car.</summary>
    public static bool Differ(string? a, string? b) => Class(a) is { } x && Class(b) is { } y && x != y;
}
