using System.Net.Http.Json;
using Mitsuke.Core;

namespace Mitsuke.Sources.TheCarApi;

// GET /api/brands?site=japan and GET /api/models?brand={slug}&site=japan, verified live on 8 Oct 2026:
// { "brands": [{ "id": 147, "name": "Porsche", "slug": "porsche", "count": 319 }] },
// { "models": [{ "name": "LANCER EVOLUTION", "slug": "lancer-evolution", "count": 18 }] }. Counts are lots listed now.
internal sealed record BrandsResponse(List<BrandDto>? Brands);

internal sealed record BrandDto(string? Name, string? Slug, int? Count);

internal sealed record ModelsResponse(List<ModelDto>? Models);

internal sealed record ModelDto(string? Name, string? Slug, int? Count);

/// <summary>Makes and models at Japanese auction, through the same resilient, rate-limited client as the searches.</summary>
public sealed class TheCarApiCatalog(TheCarApiSource source) : ICatalogSource
{
    public async Task<IReadOnlyList<CatalogMake>> GetMakesAsync(CancellationToken cancellationToken = default)
    {
        var body = await source.Http.GetFromJsonAsync<BrandsResponse>("/api/brands?site=japan", TheCarApiSource.Json, cancellationToken);
        return (body?.Brands ?? [])
            .Where(b => !string.IsNullOrWhiteSpace(b.Name) && !string.IsNullOrWhiteSpace(b.Slug))
            .Select(b => new CatalogMake(b.Name!.Trim(), b.Slug!.Trim(), b.Count ?? 0))
            .ToList();
    }

    public async Task<IReadOnlyList<CatalogModel>> GetModelsAsync(CatalogMake make, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(make);
        var body = await source.Http.GetFromJsonAsync<ModelsResponse>(
            $"/api/models?site=japan&brand={Uri.EscapeDataString(make.Slug)}", TheCarApiSource.Json, cancellationToken);
        return (body?.Models ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .Select(m => new CatalogModel(make.Name, KnownGenerations.DisplayName(m.Name!), KnownGenerations.Label(m.Name!), m.Slug?.Trim() ?? "", m.Count ?? 0))
            .ToList();
    }
}
