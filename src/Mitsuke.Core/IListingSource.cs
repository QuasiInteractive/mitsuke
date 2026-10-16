namespace Mitsuke.Core;

/// <summary>Coarse filter passed to a source so it only returns plausible candidates. Precise matching happens in <see cref="WatchlistMatcher"/>.</summary>
/// <param name="ModelCodes">Search these chassis codes only (one search each) instead of every lot of the model.</param>
/// <param name="CheapestFirst">Lowest prices first, one page of <paramref name="Limit"/>: a budget-friendly "best few" search.</param>
public sealed record SourceQuery(string Make, string Model, int? YearFrom = null, int? YearTo = null,
    IReadOnlyList<string>? ModelCodes = null, int? MaxMileageKm = null, bool CheapestFirst = false, int? Limit = null);

/// <summary>A swappable data source (TheCarApi, alert inbox, Trade Me...). Each one outputs normalised <see cref="Listing"/>s.</summary>
public interface IListingSource
{
    string Name { get; }

    IAsyncEnumerable<Listing> SearchAsync(SourceQuery query, CancellationToken cancellationToken = default);
}

/// <summary>A source with past (ended) listings, used to seed price comparisons. Optional per source.</summary>
public interface IArchiveSource
{
    string Name { get; }

    IAsyncEnumerable<Listing> SearchArchiveAsync(SourceQuery query, CancellationToken cancellationToken = default);
}
