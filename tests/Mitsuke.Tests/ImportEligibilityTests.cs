using Kensaya.Worker.Core.Rules;
using Microsoft.Extensions.Time.Testing;
using Mitsuke.Core;
using Mitsuke.Pricing;

namespace Mitsuke.Tests;

public class ImportEligibilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly KensayaEligibilityChecker Checker = new(new FileRulesSource(Landed.Catalog), new FakeTimeProvider(Now));

    private static Listing Car(int? year) => Fixtures.Gtr() with { Year = year };

    [Fact]
    public async Task A_1991_car_is_eligible_under_the_25_year_rule()
    {
        var e = (await Checker.CheckAsync(Car(1991), "AU"))!;

        Assert.Equal(EligibilityVerdict.Yes, e.Verdict);
        Assert.Contains("25-year", e.Headline, StringComparison.Ordinal);
        Assert.Equal("AU", e.Destination);
    }

    [Fact]
    public async Task A_2003_evo_needs_the_sevs_register_check()
    {
        var e = (await Checker.CheckAsync(Car(2003), "au"))!;

        Assert.Equal(EligibilityVerdict.Maybe, e.Verdict);
        Assert.Equal("Possible via SEVS register", e.Headline);
        Assert.Equal(EligibilityVerdict.No, e.Pathways.Single(p => p.Name.Contains("25-year", StringComparison.Ordinal)).Verdict);
        Assert.Contains(e.Links, l => l.Label.Contains("SEV", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_destinations_have_no_answer() => Assert.Null(await Checker.CheckAsync(Car(1991), "ZZ"));

    [Fact]
    public async Task Alerts_say_whether_the_car_can_be_imported()
    {
        var watchlist = new Watchlist { Id = Guid.NewGuid(), Name = "Evo", Make = "Nissan", Model = "Skyline" };
        var sevs = await Checker.CheckAsync(Car(2003), "AU");
        var old = await Checker.CheckAsync(Car(1991), "AU");

        Assert.Contains("? Import to AU: Possible via SEVS register. Check before bidding.", AlertFormatter.Format(watchlist, Car(2003), import: sevs), StringComparison.Ordinal);
        Assert.Contains("✓ Import to AU: Likely eligible via", AlertFormatter.Format(watchlist, Car(1991), import: old), StringComparison.Ordinal);

        Assert.Contains("Import: possible via SEVS register", PushFormatter.Format(Car(2003), null, null, null, null, sevs).Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Import", PushFormatter.Format(Car(1991), null, null, null, null, old).Body, StringComparison.Ordinal); // short when it's fine

        var email = AlertEmailFormatter.Format("a@example.com", watchlist, Car(2003), null, null, null, null, null, null, sevs);
        Assert.Contains("Import to AU:", email.Html, StringComparison.Ordinal);
        Assert.Contains("Possible via SEVS register.", email.Text, StringComparison.Ordinal);
    }
}
