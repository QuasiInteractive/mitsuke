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
    : IListingSource
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public string Name => JapanListingMapper.SourceName;

    public async IAsyncEnumerable<Listing> SearchAsync(SourceQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var opts = options.Value;

        for (var page = 0; page < opts.MaxPagesPerSearch; page++)
        {
            var offset = page * opts.PageSize;
            var url = BuildSearchUrl(query, offset, opts.PageSize);

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

    internal static string BuildSearchUrl(SourceQuery query, int offset, int limit)
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
        return "/api/search?" + string.Join('&', parts);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "TheCarApi search offset {Offset}: {Rows} rows of {Total} (request {RequestId})")]
    private static partial void LogPage(ILogger logger, int offset, int rows, long? total, string? requestId);

    [LoggerMessage(Level = LogLevel.Error, Message = "TheCarApi search returned {Status} for {Url} (request {RequestId}): {Body}")]
    private static partial void LogSearchFailed(ILogger logger, int status, string url, string? requestId, string body);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped TheCarApi row {AuctionId}: missing id, make or model")]
    private static partial void LogSkippedRow(ILogger logger, string? auctionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopped after {MaxPages} pages for {Make} {Model}; narrow the watchlist or raise MaxPagesPerSearch")]
    private static partial void LogPageCapHit(ILogger logger, int maxPages, string make, string model);
}
