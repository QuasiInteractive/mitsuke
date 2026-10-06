using System.Text.Json;
using Mitsuke.Core;
using Mitsuke.Sources.TheCarApi;

namespace Mitsuke.Tests;

public class JapanListingMapperTests
{
    private static readonly Uri Base = new("https://api.thecarapi.com");
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static Listing Map(string json) =>
        JapanListingMapper.Map(JsonSerializer.Deserialize<AuctionRow>(json, TheCarApiSource.Json)!, Base, Now)!;

    [Fact]
    public void Keeps_ids_beyond_double_precision_exact()
    {
        var listing = Map(Fixtures.GtrRow);
        Assert.Equal(new ListingKey("thecarapi-japan", Fixtures.BigId), listing.Key);
    }

    [Fact]
    public void Maps_the_core_fields()
    {
        var l = Map(Fixtures.GtrRow);

        Assert.Equal("Nissan", l.Make);
        Assert.Equal("Skyline", l.Model);
        Assert.Equal("BNR32", l.ModelCode);
        Assert.False(l.IsModified);
        Assert.Equal("BNR32-000001", l.FrameNumber);
        Assert.Equal(1991, l.Year);
        Assert.Equal(87_000, l.MileageKm);
        Assert.Equal(4m, l.Grade!.Score);
        Assert.Equal("USS Nagoya Hokuriku", l.AuctionHouse);
        Assert.Equal("1234", l.LotNumber);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero), l.AuctionEndsAt);
        Assert.True(l.RightHandDrive);
        Assert.Equal(Now, l.ObservedAt);
    }

    [Fact]
    public void Uses_native_yen_as_an_opening_bid_and_ignores_public_eur_price()
    {
        var l = Map(Fixtures.GtrRow);
        Assert.Equal(new Money(4_500_000m, "JPY"), l.Price);
        Assert.Equal(PriceKind.OpeningBid, l.PriceKind);
    }

    [Fact]
    public void Missing_native_price_is_unknown_not_zero()
    {
        var l = Map(Fixtures.GtrRow.Replace("\"native_prices\": { \"current_price\": { \"amount\": 4500000, \"currency\": \"JPY\" } }", "\"native_prices\": null", StringComparison.Ordinal));
        Assert.Null(l.Price);
        Assert.Equal(PriceKind.Unknown, l.PriceKind);
    }

    [Fact]
    public void Reads_amounts_sent_as_exponent_strings()
    {
        var l = Map(Fixtures.GtrRow.Replace("\"amount\": 4500000", "\"amount\": \"3.98E+6\"", StringComparison.Ordinal));
        Assert.Equal(3_980_000m, l.Price!.Value.Amount);
    }

    [Fact]
    public void Resolves_photo_urls_against_the_api_host_preferring_served_url()
    {
        var l = Map(Fixtures.GtrRow);
        Assert.Equal(
            [new Uri($"https://api.thecarapi.com/auction-photo/japan/{Fixtures.BigId}/0"), new Uri("https://api.thecarapi.com/reseller-image?url=y")],
            l.PhotoUrls);
    }

    [Fact]
    public void Attribution_names_the_auction_house()
    {
        Assert.Equal("USS Nagoya Hokuriku auction via TheCarApi", Map(Fixtures.GtrRow).Attribution);
    }

    [Theory]
    [InlineData("BNR32カイ", "BNR32", true)]
    [InlineData("BNR32", "BNR32", false)]
    [InlineData(" HCR32 ", "HCR32", false)]
    [InlineData(null, null, false)]
    public void Splits_the_modified_suffix_off_model_codes(string? raw, string? code, bool modified)
    {
        Assert.Equal((code, modified), JapanListingMapper.SplitModelCode(raw));
    }

    [Theory]
    [InlineData("uss_tokyo", "USS Tokyo")]
    [InlineData("uss_haa_kobe", "USS HAA Kobe")]
    [InlineData("ju_kanagawa", "JU Kanagawa")]
    [InlineData("bayauc", "BayAuc")]
    [InlineData(null, null)]
    public void Formats_auction_house_names(string? slug, string? expected)
    {
        Assert.Equal(expected, JapanListingMapper.AuctionHouseName(slug));
    }

    [Fact]
    public void Rows_without_an_id_are_skipped()
    {
        var row = JsonSerializer.Deserialize<AuctionRow>(Fixtures.GtrRow, TheCarApiSource.Json)! with { AuctionIdStr = null };
        Assert.Null(JapanListingMapper.Map(row, Base, Now));
    }
}
