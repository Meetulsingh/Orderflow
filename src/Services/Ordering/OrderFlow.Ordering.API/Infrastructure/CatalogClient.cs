namespace OrderFlow.Ordering.API.Infrastructure;

public sealed record CatalogProduct(Guid Id, string Name, decimal Price, int Stock, string? ImageUrl = null);

/// <summary>Typed HTTP client for the Catalog service, wrapped in a standard resilience pipeline (retry, circuit breaker, timeouts).</summary>
public sealed class CatalogClient(HttpClient http)
{
    private sealed record Page(IReadOnlyList<CatalogProduct> Items);

    public async Task<IReadOnlyList<CatalogProduct>> LookupAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/api/products/lookup", new { ids }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CatalogProduct>>(ct) ?? [];
    }

    /// <summary>The whole catalog (it fits in one page), used to pick a campaign's deal products.</summary>
    public async Task<IReadOnlyList<CatalogProduct>> ListAsync(CancellationToken ct)
    {
        var page = await http.GetFromJsonAsync<Page>("/api/products?pageSize=48&sort=rating", ct);
        return page?.Items ?? [];
    }
}
