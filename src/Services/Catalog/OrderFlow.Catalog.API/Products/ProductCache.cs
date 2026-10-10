using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace OrderFlow.Catalog.API.Products;

/// <summary>
/// Cache-aside wrapper over Redis. Rather than tracking every cached query key, all keys embed a
/// catalog "generation"; bumping the generation on any write invalidates every cached page at once.
/// </summary>
public sealed class ProductCache(IDistributedCache cache, ILogger<ProductCache> logger)
{
    private const string GenerationKey = "catalog:generation";
    private static readonly DistributedCacheEntryOptions EntryOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
    };

    public async Task<(T Value, bool Hit)> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, CancellationToken ct)
    {
        string? fullKey = null;
        try
        {
            fullKey = $"catalog:{await GetGenerationAsync(ct)}:{key}";
            var cached = await cache.GetStringAsync(fullKey, ct);
            if (cached is not null)
                return (JsonSerializer.Deserialize<T>(cached, JsonSerializerOptions.Web)!, true);
        }
        catch (Exception ex)
        {
            // Cache is an optimisation: degrade gracefully to the database if Redis is unavailable.
            logger.LogWarning(ex, "Cache read failed for {Key}", key);
        }

        var value = await factory();
        if (fullKey is not null)
        {
            try
            {
                await cache.SetStringAsync(fullKey, JsonSerializer.Serialize(value, JsonSerializerOptions.Web), EntryOptions, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cache write failed for {Key}", key);
            }
        }
        return (value, false);
    }

    public async Task InvalidateAsync(CancellationToken ct = default)
    {
        try
        {
            await cache.SetStringAsync(GenerationKey, Guid.NewGuid().ToString("N")[..8], ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache invalidation failed");
        }
    }

    private async Task<string> GetGenerationAsync(CancellationToken ct)
    {
        var generation = await cache.GetStringAsync(GenerationKey, ct);
        if (generation is not null) return generation;
        generation = "g0";
        await cache.SetStringAsync(GenerationKey, generation, ct);
        return generation;
    }
}
