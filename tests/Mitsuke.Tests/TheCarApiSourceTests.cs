using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Mitsuke.Core;
using Mitsuke.Sources.TheCarApi;

namespace Mitsuke.Tests;

public class TheCarApiSourceTests
{
    private static readonly SourceQuery R32 = new("Nissan", "Skyline", 1989, 1994);

    [Fact]
    public void Search_url_targets_japan_by_production_year()
    {
        Assert.Equal(
            "/api/search?site=japan&brand=Nissan&model=Skyline&production_year_from=1989&production_year_to=1994&limit=100&offset=200",
            TheCarApiSource.BuildSearchUrl(R32, offset: 200, limit: 100));
    }

    [Fact]
    public void Search_url_escapes_values()
    {
        Assert.Contains("brand=Mercedes%20Benz&model=C%26D", TheCarApiSource.BuildSearchUrl(new SourceQuery("Mercedes Benz", "C&D"), 0, 10), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pages_until_the_total_is_reached()
    {
        var handler = new StubHandler(
            _ => Ok(Fixtures.SearchPage(3, Row("1"), Row("2"))),
            _ => Ok(Fixtures.SearchPage(3, Row("3"))));
        var source = CreateSource(handler, pageSize: 2);

        var ids = await source.SearchAsync(R32).Select(l => l.Key.SourceId).ToListAsync();

        Assert.Equal(["1", "2", "3"], ids);
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith("offset=2", handler.Requests[1].RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Never_exceeds_the_page_cap()
    {
        // A server that always claims more results must not turn into a crawl.
        var handler = new StubHandler(_ => Ok(Fixtures.SearchPage(1_000_000, Row("7"), Row("8"))));
        var source = CreateSource(handler, pageSize: 2, maxPages: 3);

        await source.SearchAsync(R32).ToListAsync();

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Client_errors_surface_instead_of_returning_nothing()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"unknown site"}""") });
        var source = CreateSource(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.SearchAsync(R32).ToListAsync().AsTask());
    }

    [Fact]
    public async Task Pipeline_sends_the_key_and_retries_transient_failures()
    {
        var handler = new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => Ok(Fixtures.SearchPage(1, Row("1"))));

        var services = new ServiceCollection().AddLogging();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["THECARAPI_KEY"] = "test-key" }).Build();
        services.AddTheCarApiSource(config);
        services.AddHttpClient<TheCarApiSource>().ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        var listings = await provider.GetRequiredService<IListingSource>().SearchAsync(R32).ToListAsync();

        Assert.Single(listings);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("test-key", r.Headers.GetValues("X-API-Key").Single()));
    }

    [Fact]
    public void Missing_key_fails_at_startup()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddTheCarApiSource(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<TheCarApiOptions>>().Value);
        Assert.Contains("THECARAPI_KEY", ex.Message, StringComparison.Ordinal);
    }

    private static string Row(string id) => Fixtures.GtrRow.Replace(Fixtures.BigId, id, StringComparison.Ordinal);

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static TheCarApiSource CreateSource(StubHandler handler, int pageSize = 100, int maxPages = 5) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.thecarapi.com") },
            Options.Create(new TheCarApiOptions { ApiKey = "k", PageSize = pageSize, MaxPagesPerSearch = maxPages }),
            TimeProvider.System,
            NullLogger<TheCarApiSource>.Instance);

    /// <summary>Returns the queued responses in order, repeating the last one.</summary>
    private sealed class StubHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var next = responses[Math.Min(Requests.Count - 1, responses.Length - 1)];
            return Task.FromResult(next(request));
        }
    }
}
