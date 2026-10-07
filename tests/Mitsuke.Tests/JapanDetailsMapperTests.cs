using System.Text.Json;
using Mitsuke.Core;
using Mitsuke.Sources.TheCarApi;

namespace Mitsuke.Tests;

public class JapanDetailsMapperTests
{
    private static readonly Uri Base = new("https://api.thecarapi.com");
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
    private static readonly ListingKey Key = new("thecarapi-japan", "123");

    // Hand-written, shaped like GET /api/auction/japan/{id}: snake_case row, PascalCase car_identification keys.
    private const string Detail = """
        {
          "success": true,
          "auction": {
            "auction_id_str": "123",
            "cylinder_capacity": 2600,
            "images": [ { "served_url": "/auction-photo/japan/123/0" }, { "url": "/reseller-image?url=b" } ],
            "car_identification": {
              "InteriorGrade": "C",
              "InspectionReports": [
                { "type": "auction_sheet", "url": "/report-vault/japan/123/now.jpg" },
                { "type": "previous_auction_sheet", "url": "/report-vault/japan/123/old.jpg", "auction_date": "2026-09-04" },
                { "type": "something_else", "url": "/x.pdf" }
              ],
              "JapanRelists": [
                {
                  "auction_date": "2026-09-04", "auction_grade": "3.5", "venue": "uss_nagoya_hokuriku",
                  "lot_number": "35367", "match": "probable",
                  "opening_price": { "amount": "3.98E+6", "currency": "JPY" },
                  "fields": { "mileage_km": 48000 },
                  "changes": ["mileage_km"]
                },
                {
                  "auction_date": "2026-10-02", "auction_grade": "3.5", "venue": "uss_nagoya_hokuriku",
                  "lot_number": "3087", "match": "probable",
                  "opening_price": { "amount": 3980000, "currency": "JPY" },
                  "fields": { "mileage_km": 49000 }, "changes": []
                },
                { "auction_date": "not a date" }
              ]
            }
          }
        }
        """;

    private static ListingDetails Map() =>
        JapanDetailsMapper.Map(Key, JsonSerializer.Deserialize<DetailResponse>(Detail, TheCarApiSource.Json)!.Auction!, Base, Now);

    [Fact]
    public void Maps_current_and_previous_sheets_and_ignores_other_reports()
    {
        var d = Map();
        Assert.Equal(2, d.Sheets.Count);
        Assert.Equal(new Uri("https://api.thecarapi.com/report-vault/japan/123/now.jpg"), d.CurrentSheet!.ImageUrl);
        Assert.Equal(new DateOnly(2026, 9, 4), d.Sheets.Single(s => !s.IsCurrent).AuctionDate);
    }

    [Fact]
    public void Maps_relists_newest_first_and_skips_undated_ones()
    {
        var d = Map();
        Assert.Equal([new DateOnly(2026, 10, 2), new DateOnly(2026, 9, 4)], d.Relists.Select(r => r.AuctionDate));

        var oldest = d.Relists[^1];
        Assert.Equal("USS Nagoya Hokuriku", oldest.AuctionHouse);
        Assert.Equal(new Money(3_980_000m, "JPY"), oldest.OpeningBid);
        Assert.Equal(48_000, oldest.MileageKm);
        Assert.Equal("probable", oldest.Confidence);
        Assert.Equal(["mileage_km"], oldest.Changes);
    }

    [Fact]
    public void Maps_interior_grade_engine_and_full_gallery()
    {
        var d = Map();
        Assert.Equal("C", d.InteriorGrade);
        Assert.Equal(2600, d.EngineCc);
        Assert.Equal(2, d.PhotoUrls.Count);
        Assert.Equal(Now, d.FetchedAt);
    }

    [Fact]
    public void Alert_mentions_relists_changes_and_the_sheet()
    {
        var watchlist = new Watchlist { Id = Guid.NewGuid(), Name = "R32", Make = "Nissan", Model = "Skyline" };
        var text = AlertFormatter.Format(watchlist, Fixtures.Gtr(), Map());

        Assert.Contains("Seen at auction 2 times before, since 4 Sep", text, StringComparison.Ordinal);
        Assert.Contains("Check: mileage_km changed between auctions.", text, StringComparison.Ordinal);
        Assert.Contains("Auction sheet available (not yet decoded).", text, StringComparison.Ordinal);
    }
}
