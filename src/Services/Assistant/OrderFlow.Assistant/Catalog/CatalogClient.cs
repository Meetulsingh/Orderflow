using System.Net.Http.Json;

namespace OrderFlow.Assistant.Catalog;

/// <summary>Product as returned by the Catalog API (same JSON shape the frontend uses for its cart).</summary>
public sealed record CatalogProduct(
    Guid Id, string Name, string Description, string Category, decimal Price,
    int Stock, string? ImageUrl, double Rating, DateTime UpdatedAtUtc);

/// <summary>
/// Reads the catalog through the Catalog service's public, Redis-cached list endpoint. The store is small,
/// so one page holds every product and the assistant's tools filter it in memory.
/// </summary>
public sealed class CatalogClient(HttpClient http)
{
    private sealed record Page(IReadOnlyList<CatalogProduct> Items);

    public async Task<IReadOnlyList<CatalogProduct>> GetAllAsync(CancellationToken ct)
    {
        var page = await http.GetFromJsonAsync<Page>("/api/products?pageSize=48&sort=rating", ct);
        return page?.Items ?? [];
    }
}
