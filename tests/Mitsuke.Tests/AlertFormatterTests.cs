using Mitsuke.Core;

namespace Mitsuke.Tests;

public class AlertFormatterTests
{
    private static readonly Watchlist R32 = new() { Id = Guid.NewGuid(), Name = "R32 GT-R", Make = "Nissan", Model = "Skyline" };

    [Fact]
    public void Formats_a_short_alert_with_attribution_and_disclaimer()
    {
        var listing = Fixtures.Gtr() with
        {
            AuctionHouse = "USS Tokyo",
            LotNumber = "1234",
            AuctionEndsAt = new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero), // midnight JST, 10 Oct
            Attribution = "USS Tokyo auction via TheCarApi",
        };

        var text = AlertFormatter.Format(R32, listing);

        Assert.StartsWith("[R32 GT-R] 1991 Nissan Skyline BNR32. 87,000 km, grade 4.", text, StringComparison.Ordinal);
        Assert.Contains("Opening bid: ¥4,500,000", text, StringComparison.Ordinal);
        Assert.Contains("Auction Fri 9 Oct (Japan), lot 1234", text, StringComparison.Ordinal);
        Assert.Contains("USS Tokyo auction via TheCarApi", text, StringComparison.Ordinal);
        Assert.EndsWith(AlertFormatter.Disclaimer, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_so_when_there_is_no_price()
    {
        Assert.Contains("No opening price published.", AlertFormatter.Format(R32, Fixtures.Gtr(b => b.Price = null)), StringComparison.Ordinal);
    }
}
