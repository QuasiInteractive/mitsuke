using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mitsuke.Core;

namespace Mitsuke.Sources.TheCarApi;

/// <summary>Japanese auction lots from TheCarApi's <c>japan</c> source. Retries, circuit breaking and rate limiting live on the HttpClient (see <see cref="ServiceCollectionExtensions"/>).</summary>
public sealed partial class TheCarApiSource(HttpClient http, IOptions<TheCarApiOptions> options, TimeProvider clock, ILogger<TheCarApiSource> logger)
    : IListingSource, IListingDetailsSource, IArchiveSource
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public string Name => JapanListingMapper.SourceName;

    /// <summary>
    /// Model names this feed uses interchangeably. Its <c>model</c> filter is an exact match, and the same car can be
    /// filed under either name: on 8 Oct 2026 "Lancer" held four CT9A Evos while "Lancer Evolution" held one.
    /// A search for any name in a group searches them all. Add a group only after seeing it in live data.
    /// </summary>
    private static readonly string[][] ModelNameGroups =
    [
        ["Lancer Evolution", "Lancer"],
    ];

    internal static IReadOnlyList<string> ModelsToSearch(string model)
    {
        var group = ModelNameGroups.FirstOrDefault(g => g.Contains(model.Trim(), StringComparer.OrdinalIgnoreCase));
        return group is null ? [model] : [model, .. group.Where(m => !m.Equals(model.Trim(), StringComparison.OrdinalIgnoreCase))];
    }

    public IAsyncEnumerable<Listing> SearchAsync(SourceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return AcrossModelNamesAsync(query, (q, offset, limit) => BuildSearchUrl(q, offset, limit), cancellationToken);
    }

    /// <summary>Runs the search once per model name in the query's group, yielding each lot once.</summary>
    private async IAsyncEnumerable<Listing> AcrossModelNamesAsync(
        SourceQuery query, Func<SourceQuery, int, int, string> buildUrl, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seen = new HashSet<ListingKey>();
        foreach (var model in ModelsToSearch(query.Model))
        {
            var q = query with { Model = model };
            await foreach (var listing in PagedAsync(q, (offset, limit) => buildUrl(q, offset, limit), cancellationToken))
                if (seen.Add(listing.Key)) yield return listing;
        }
    }

    /// <summary>
    /// Ended lots from TheCarApi's archive (same row shape as search). Their prices are opening bids too:
    /// the archive is "not a sold-price index". Used to seed comparables for the deal score.
    /// </summary>
    public IAsyncEnumerable<Listing> SearchArchiveAsync(SourceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return AcrossModelNamesAsync(query, (q, offset, limit) => BuildSearchUrl(q, offset, limit, "/api/archive/search"), cancellationToken);
    }

    private async IAsyncEnumerable<Listing> PagedAsync(
        SourceQuery query, Func<int, int, string> buildUrl, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var opts = options.Value;

        for (var page = 0; page < opts.MaxPagesPerSearch; page++)
        {
            var offset = page * opts.PageSize;
            var url = buildUrl(offset, opts.PageSize);

            using var response = await http.GetAsync(url, cancellationToken);
            var requestId = response.Headers.TryGetValues("X-Request-ID", out var ids) ? ids.FirstOrDefault() : null;
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                LogSearchFailed(logger, (int)response.StatusCode, url, requestId, body.Length > 300 ? body[..300] : body);
                response.EnsureSuccessStatusCode();
            }

            var result = await response.Content.ReadFromJsonAsync<SearchResponse>(Json, cancellationToken)
                         ?? throw new InvalidOperationException($"Empty search response (request {requestId}).");
            LogPage(logger, offset, result.Results.Count, result.Total, requestId);

            var observedAt = clock.GetUtcNow();
            foreach (var row in result.Results)
            {
                if (JapanListingMapper.Map(row, opts.BaseAddress, observedAt) is { } listing)
                    yield return listing;
                else
                    LogSkippedRow(logger, row.AuctionIdStr);
            }

            var reachedEnd = result.Results.Count < opts.PageSize || (result.Total is { } total && offset + result.Results.Count >= total);
            if (reachedEnd) yield break;
        }

        LogPageCapHit(logger, opts.MaxPagesPerSearch, query.Make, query.Model);
    }

    public bool CanFetch(ListingKey key) => key.Source == JapanListingMapper.SourceName;

    public async Task<ListingDetails?> GetDetailsAsync(ListingKey key, CancellationToken cancellationToken = default)
    {
        if (!CanFetch(key)) throw new ArgumentException($"Not a {Name} listing: {key}", nameof(key));
        var url = $"/api/auction/japan/{Uri.EscapeDataString(key.SourceId)}";

        using var response = await http.GetAsync(url, cancellationToken);
        var requestId = response.Headers.TryGetValues("X-Request-ID", out var ids) ? ids.FirstOrDefault() : null;
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            LogDetailMissing(logger, key.SourceId, requestId);
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            LogSearchFailed(logger, (int)response.StatusCode, url, requestId, body.Length > 300 ? body[..300] : body);
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<DetailResponse>(Json, cancellationToken);
        if (result?.Auction is not { } auction) return null;

        var details = JapanDetailsMapper.Map(key, auction, options.Value.BaseAddress, clock.GetUtcNow());
        LogDetail(logger, key.SourceId, details.Sheets.Count, details.Relists.Count, requestId);
        return details;
    }

    internal static string BuildSearchUrl(SourceQuery query, int offset, int limit, string path = "/api/search")
    {
        var parts = new List<string>
        {
            "site=japan",
            $"brand={Uri.EscapeDataString(query.Make)}",
            $"model={Uri.EscapeDataString(query.Model)}",
        };
        // Production year, not registration year: imports are often registered years after they were built.
        if (query.YearFrom is { } from) parts.Add(string.Create(CultureInfo.InvariantCulture, $"production_year_from={from}"));
        if (query.YearTo is { } to) parts.Add(string.Create(CultureInfo.InvariantCulture, $"production_year_to={to}"));
        parts.Add(string.Create(CultureInfo.InvariantCulture, $"limit={limit}"));
        parts.Add(string.Create(CultureInfo.InvariantCulture, $"offset={offset}"));
        return path + "?" + string.Join('&', parts);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "TheCarApi search offset {Offset}: {Rows} rows of {Total} (request {RequestId})")]
    private static partial void LogPage(ILogger logger, int offset, int rows, long? total, string? requestId);

    [LoggerMessage(Level = LogLevel.Information, Message = "TheCarApi detail {AuctionId}: {Sheets} sheets, {Relists} relists (request {RequestId})")]
    private static partial void LogDetail(ILogger logger, string auctionId, int sheets, int relists, string? requestId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TheCarApi detail {AuctionId} not found (request {RequestId})")]
    private static partial void LogDetailMissing(ILogger logger, string auctionId, string? requestId);

    [LoggerMessage(Level = LogLevel.Error, Message = "TheCarApi request returned {Status} for {Url} (request {RequestId}): {Body}")]
    private static partial void LogSearchFailed(ILogger logger, int status, string url, string? requestId, string body);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped TheCarApi row {AuctionId}: missing id, make or model")]
    private static partial void LogSkippedRow(ILogger logger, string? auctionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopped after {MaxPages} pages for {Make} {Model}; narrow the watchlist or raise MaxPagesPerSearch")]
    private static partial void LogPageCapHit(ILogger logger, int maxPages, string make, string model);
}
