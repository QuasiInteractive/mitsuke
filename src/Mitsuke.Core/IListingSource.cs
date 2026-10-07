namespace Mitsuke.Core;

/// <summary>Coarse filter passed to a source so it only returns plausible candidates. Precise matching happens in <see cref="WatchlistMatcher"/>.</summary>
public sealed record SourceQuery(string Make, string Model, int? YearFrom = null, int? YearTo = null);

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
