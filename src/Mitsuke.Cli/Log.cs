using Microsoft.Extensions.Logging;

namespace Mitsuke.Cli;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "{Key} rejected: {Reasons}")]
    public static partial void Rejected(ILogger logger, Mitsuke.Core.ListingKey key, IReadOnlyList<string> reasons);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scan done: {Seen} listings checked, {Matched} matched '{Watchlist}', sent via {Channel}")]
    public static partial void ScanDone(ILogger logger, int seen, int matched, string watchlist, string channel);
}
