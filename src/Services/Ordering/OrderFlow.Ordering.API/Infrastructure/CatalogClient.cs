namespace OrderFlow.Ordering.API.Infrastructure;

public sealed record CatalogProduct(Guid Id, string Name, decimal Price, int Stock);

/// <summary>Typed HTTP client for the Catalog service, wrapped in a standard resilience pipeline (retry, circuit breaker, timeouts).</summary>
public sealed class CatalogClient(HttpClient http)
{
    public async Task<IReadOnlyList<CatalogProduct>> LookupAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/api/products/lookup", new { ids }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CatalogProduct>>(ct) ?? [];
    }
}
