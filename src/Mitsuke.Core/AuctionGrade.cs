using System.Globalization;

namespace Mitsuke.Core;

/// <summary>
/// Japanese auction grade. Numeric grades run 1–6 (S is treated as 6); R/RA mean the car has had
/// accident repairs and sit outside the numeric scale; anything else (e.g. "***") is ungraded.
/// </summary>
public sealed record AuctionGrade(string Raw, decimal? Score, bool IsRepaired)
{
    public static AuctionGrade? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim().ToUpperInvariant();

        return value switch
        {
            "S" => new AuctionGrade(value, 6m, IsRepaired: false),
            "R" or "RA" => new AuctionGrade(value, null, IsRepaired: true),
            _ when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var score)
                => new AuctionGrade(value, score, IsRepaired: false),
            _ => new AuctionGrade(value, null, IsRepaired: false),
        };
    }

    public override string ToString() => Raw;
}
