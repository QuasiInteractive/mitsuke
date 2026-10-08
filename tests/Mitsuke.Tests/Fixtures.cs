using Mitsuke.Core;

namespace Mitsuke.Tests;

/// <summary>
/// Hand-written fixtures shaped like real TheCarApi responses. Not copied API data: the repo is public
/// and TheCarApi's terms forbid redistributing raw feeds.
/// </summary>
internal static class Fixtures
{
    // Larger than 2^53 on purpose: parsing it as a double would corrupt it.
    public const string BigId = "9007199254740993";

    public const string GtrRow = $$"""
        {
          "auction_id": {{BigId}},
          "auction_id_str": "{{BigId}}",
          "site_name": "japan",
          "clean_make": "NISSAN",
          "clean_model": "SKYLINE",
          "model_code": "BNR32",
          "frame_number": "BNR32-000001",
          "production_year": 1991,
          "production_month": 8,
          "registration_year": 1991,
          "mileage": 87000,
          "auction_grade": "4",
          "auction_house": "uss_nagoya_hokuriku",
          "lot_number": "1234",
          "auction_end_at": "2026-10-09T15:00:00Z",
          "gearbox_group": "Manual",
          "fuel_group": "Petrol",
          "steering": "right",
          "public_price_eur": 99999.99,
          "native_prices": { "current_price": { "amount": 4500000, "currency": "JPY" } },
          "images": [
            { "url": "/reseller-image?url=x", "served_url": "/auction-photo/japan/{{BigId}}/0" },
            { "url": "/reseller-image?url=y" }
          ]
        }
        """;

    public static string SearchPage(int total, params string[] rows) =>
        $$"""{ "success": true, "total": {{total}}, "results": [{{string.Join(',', rows)}}] }""";

    public static Listing Gtr(Action<ListingBuilder>? tweak = null)
    {
        var b = new ListingBuilder();
        tweak?.Invoke(b);
        return b.Build();
    }

    internal sealed class ListingBuilder
    {
        public string Make = "Nissan", Model = "Skyline";
        public string? ModelCode = "BNR32";
        public bool IsModified;
        public int? Year = 1991, MileageKm = 87_000;
        public string? Grade = "4";
        public Money? Price = new Money(4_500_000m, "JPY");

        public Listing Build() => new()
        {
            Key = new ListingKey("test", "1"),
            Make = Make,
            Model = Model,
            ModelCode = ModelCode,
            IsModified = IsModified,
            Year = Year,
            MileageKm = MileageKm,
            Grade = AuctionGrade.Parse(Grade),
            Price = Price,
            PriceKind = PriceKind.OpeningBid,
            Attribution = "test",
            ObservedAt = DateTimeOffset.UnixEpoch,
        };
    }
}
