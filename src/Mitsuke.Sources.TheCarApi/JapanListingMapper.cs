using System.Globalization;
using Mitsuke.Core;

namespace Mitsuke.Sources.TheCarApi;

/// <summary>Turns a TheCarApi <c>japan</c> search row into a <see cref="Listing"/>. Pure, so it is unit-tested without HTTP.</summary>
internal static class JapanListingMapper
{
    public const string SourceName = "thecarapi-japan";

    // Japanese auction sheets mark modified cars with カイ ("kai", modified) after the model code.
    private const string ModifiedSuffix = "カイ";

    public static Listing? Map(AuctionRow row, Uri baseAddress, DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(row.AuctionIdStr) || row.CleanMake is null || row.CleanModel is null)
            return null;

        var (modelCode, isModified) = SplitModelCode(row.ModelCode);
        var house = AuctionHouseName(row.AuctionHouse);

        return new Listing
        {
            Key = new ListingKey(SourceName, row.AuctionIdStr),
            Make = Title(row.CleanMake),
            Model = Title(row.CleanModel),
            ModelCode = modelCode,
            IsModified = isModified,
            FrameNumber = string.IsNullOrWhiteSpace(row.FrameNumber) ? null : row.FrameNumber.Trim(),
            Year = row.ProductionYear ?? row.RegistrationYear,
            MileageKm = row.Mileage is { } km ? (int)Math.Round(km) : null,
            Grade = AuctionGrade.Parse(row.AuctionGrade),
            // Only the native amount is trustworthy. public_price_eur carries a reseller markup on live lots.
            Price = row.NativePrices?.CurrentPrice is { Amount: { } amount, Currency: { } currency }
                ? new Money(amount, currency.ToUpperInvariant())
                : null,
            // On the japan source current_price is the opening bid, never a result (TheCarApi docs, "Sources").
            PriceKind = row.NativePrices?.CurrentPrice?.Amount is null ? PriceKind.Unknown : PriceKind.OpeningBid,
            AuctionHouse = house,
            LotNumber = row.LotNumber,
            AuctionEndsAt = row.AuctionEndAt,
            Transmission = row.GearboxGroup,
            Fuel = row.FuelGroup,
            RightHandDrive = row.Steering?.ToUpperInvariant() switch
            {
                "RIGHT" or "RHD" => true,
                "LEFT" or "LHD" => false,
                _ => null,
            },
            PhotoUrls = (row.Images ?? [])
                .Select(i => i.ServedUrl ?? i.Url)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => new Uri(baseAddress, u))
                .ToList(),
            Attribution = house is null ? "Japanese auction via TheCarApi" : $"{house} auction via TheCarApi",
            ObservedAt = observedAt,
        };
    }

    internal static (string? Code, bool IsModified) SplitModelCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, false);
        var code = raw.Trim();
        return code.EndsWith(ModifiedSuffix, StringComparison.Ordinal)
            ? (code[..^ModifiedSuffix.Length].TrimEnd(), true)
            : (code, false);
    }

    // "uss_nagoya_hokuriku" -> "USS Nagoya Hokuriku", "ju_kanagawa" -> "JU Kanagawa".
    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
        { "uss", "ju", "taa", "caa", "haa", "jaa", "iaa", "laa", "ns", "kcaa", "zip" };

    internal static string? AuctionHouseName(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        if (slug.Equals("bayauc", StringComparison.OrdinalIgnoreCase)) return "BayAuc";
        if (slug.Equals("aucnet", StringComparison.OrdinalIgnoreCase)) return "AUCNET";
        if (slug.Equals("arai", StringComparison.OrdinalIgnoreCase)) return "Arai";

        return string.Join(' ', slug.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Acronyms.Contains(part) ? part.ToUpperInvariant() : Title(part)));
    }

    private static string Title(string s) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Trim().ToLowerInvariant());
}
