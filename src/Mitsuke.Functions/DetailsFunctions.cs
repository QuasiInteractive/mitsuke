using Microsoft.Azure.Functions.Worker;
using Mitsuke.Core;

namespace Mitsuke.Functions;

/// <summary>A request to bring one listing's saved details up to date. Sent by the API when a lot page shows stale ones.</summary>
public sealed record DetailsRefreshRequest(Guid ListingId);

/// <summary>Refreshes listing details off the request path; see <see cref="DetailsRefresher"/>.</summary>
public sealed class DetailsFunctions(DetailsRefresher refresher)
{
    public const string DetailsQueue = "details";

    [Function(nameof(RefreshDetails))]
    public async Task RefreshDetails([QueueTrigger(DetailsQueue)] DetailsRefreshRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await refresher.RefreshAsync(request.ListingId, cancellationToken);
    }
}
