using Microsoft.Azure.Functions.Worker;
using Mitsuke.Core;

namespace Mitsuke.Functions;

/// <summary>Refreshes the makes and models the new-watchlist form offers. Daily at 04:00 Brisbane; about 75 requests.</summary>
public sealed class CatalogFunctions(CatalogSync sync)
{
    [Function(nameof(SyncCatalog))]
    public async Task SyncCatalog([TimerTrigger("0 0 18 * * *")] TimerInfo timer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timer);
        await sync.RunAsync(cancellationToken);
    }
}
