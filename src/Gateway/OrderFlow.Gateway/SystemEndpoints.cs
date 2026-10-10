using System.Diagnostics;
using System.Text.Json;

namespace OrderFlow.Gateway;

public sealed record ServiceHealth(string Name, string Status, double? LatencyMs, JsonElement? Details);

/// <summary>Gateway-owned endpoints: aggregated health for the admin dashboard and a warm-up trigger for sleeping free-tier services.</summary>
public static class SystemEndpoints
{
    private static long _lastWarmupTicks;

    public static void MapSystemEndpoints(this WebApplication app, IReadOnlyList<DownstreamService> services)
    {
        var group = app.MapGroup("/api/system").WithTags("System");

        group.MapGet("/health", async (IHttpClientFactory factory, CancellationToken ct) =>
        {
            var client = factory.CreateClient("health");
            var results = await Task.WhenAll(services.Select(s => Probe(client, s, ct)));
            var gateway = new ServiceHealth("gateway", "Healthy", 0, null);
            var all = results.Prepend(gateway).ToList();
            var overall = all.All(r => r.Status == "Healthy") ? "Healthy"
                : all.Any(r => r.Status == "Healthy") ? "Degraded" : "Unhealthy";
            return Results.Ok(new { status = overall, checkedAtUtc = DateTime.UtcNow, services = all });
        }).WithSummary("Aggregated readiness of every microservice");

        group.MapPost("/warmup", (IHttpClientFactory factory, ILoggerFactory loggers) =>
        {
            // Debounce: at most one wake-up wave per 30 seconds regardless of how many browsers ask.
            var now = DateTime.UtcNow.Ticks;
            var last = Interlocked.Read(ref _lastWarmupTicks);
            if (now - last < TimeSpan.FromSeconds(30).Ticks || Interlocked.CompareExchange(ref _lastWarmupTicks, now, last) != last)
                return Results.Accepted();

            var logger = loggers.CreateLogger("Warmup");
            var client = factory.CreateClient("warmup");
            foreach (var service in services)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var response = await client.GetAsync($"{service.Address}/health");
                        logger.LogInformation("Warm-up {Service}: {Status}", service.Name, (int)response.StatusCode);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Warm-up {Service} failed: {Message}", service.Name, ex.Message);
                    }
                });
            }
            return Results.Accepted();
        }).WithSummary("Wake up sleeping services (free hosting tiers spin down when idle)");
    }

    private static async Task<ServiceHealth> Probe(HttpClient client, DownstreamService service, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.GetAsync($"{service.Address}/health/ready", ct);
            var elapsed = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 0);
            JsonElement? details = null;
            if (response.Content.Headers.ContentType?.MediaType == "application/json")
                details = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var status = details?.TryGetProperty("status", out var s) == true ? s.GetString() ?? "Unknown"
                : response.IsSuccessStatusCode ? "Healthy" : "Unhealthy";
            return new ServiceHealth(service.Name, status, elapsed, details);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new ServiceHealth(service.Name, "Unreachable", null, null);
        }
    }
}
