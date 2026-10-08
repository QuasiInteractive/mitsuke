using Mitsuke.Core;

namespace Mitsuke.Api;

/// <summary>
/// What the new-watchlist form offers: makes and models at auction (synced daily by the pipeline), and the
/// generations of a model with their chassis codes and years. Public and cacheable: it's the same for everyone.
/// </summary>
public static class CatalogEndpoints
{
    public static RouteGroupBuilder MapCatalogEndpoints(this RouteGroupBuilder api)
    {
        var catalog = api.MapGroup("/catalog").AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "public, max-age=3600";
            return await next(context);
        });

        catalog.MapGet("/makes", async (ICatalogStore store, CancellationToken ct) => TypedResults.Ok(await store.GetMakesAsync(ct)));

        catalog.MapGet("/models", async (string make, ICatalogStore store, CancellationToken ct) =>
            TypedResults.Ok(await store.GetModelsAsync(make, ct)));

        catalog.MapGet("/generations", async (string make, string model, ICatalogStore store, CancellationToken ct) =>
            TypedResults.Ok(KnownGenerations.Merge(make, model, await store.GetObservedGenerationsAsync(make, model, ct))));

        return api;
    }
}
