using Mitsuke.Core;

namespace Mitsuke.Tests;

public class ListingIdentityTests
{
    private static readonly DateTimeOffset AuctionEnd = new(2026, 10, 9, 15, 0, 0, TimeSpan.Zero);

    // The real pair from 8 Oct 2026: one lot, two feed copies, venues named differently, only one priced.
    private static Listing Copy(string id, string venue, Money? price) => Fixtures.Gtr(b => { b.Make = "BMW"; b.Model = "M3"; b.ModelCode = "BL32"; }) with
    {
        Key = new ListingKey("thecarapi-japan", id), Year = 2001, MileageKm = 27_000, LotNumber = "58212",
        AuctionHouse = venue, AuctionEndsAt = AuctionEnd, Price = price,
    };

    [Fact]
    public void Two_copies_of_one_lot_are_one_car_and_the_priced_copy_is_kept()
    {
        var unpriced = Copy("9001159947", "USS Nagoya", null);
        var priced = Copy("9001102582", "USS Nagoya Hokuriku", new Money(3_500_000m, "JPY"));

        Assert.Equal(ListingIdentity.SameLotKey(unpriced), ListingIdentity.SameLotKey(priced));
        Assert.Equal([priced.Key], ListingIdentity.OnePerCar([unpriced, priced], l => l).Select(l => l.Key));
        Assert.Equal([priced.Key], ListingIdentity.OnePerCar([priced, unpriced], l => l).Select(l => l.Key)); // order doesn't matter
    }

    [Fact]
    public void Different_mileage_or_lot_means_different_cars()
    {
        var a = Copy("1", "USS Nagoya", null);
        Assert.Equal(3, ListingIdentity.OnePerCar([a, a with { Key = new ListingKey("t", "2"), MileageKm = 28_000 }, a with { Key = new ListingKey("t", "3"), LotNumber = "58213" }], l => l).Count());
    }

    [Theory]
    [InlineData("Host=h;Database=d", 4)]
    [InlineData("Host=h;Database=d;Maximum Pool Size=12", 12)]
    public void Each_process_takes_only_a_few_database_connections(string connection, int pool) =>
        Assert.Equal(pool, new Npgsql.NpgsqlConnectionStringBuilder(Mitsuke.Data.ServiceCollectionExtensions.WithPoolCap(connection)).MaxPoolSize);

    [Fact]
    public void Lots_without_a_number_are_never_merged() =>
        Assert.Equal(2, ListingIdentity.OnePerCar([Fixtures.Gtr() with { LotNumber = null }, Fixtures.Gtr() with { LotNumber = null, Key = new ListingKey("t", "x") }], l => l).Count());
}
