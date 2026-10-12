namespace OrderFlow.Gateway;

/// <summary>
/// Free-tier hosts put a service to sleep after 15 minutes without inbound HTTP traffic. While the gateway is
/// awake (kept up by an external schedule or by visitors), it pings the services every page needs so they stay
/// awake too. Outbound requests don't count as traffic to the gateway, so it still sleeps when the schedule ends.
/// </summary>
public sealed class KeepWarmService(
    IHttpClientFactory factory,
    IReadOnlyList<DownstreamService> services,
    IConfiguration configuration,
    ILogger<KeepWarmService> logger) : BackgroundService
{
    // Comfortably under the 15-minute idle limit.
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var names = (configuration["KEEP_WARM_SERVICES"] ?? "catalog")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var targets = services.Where(s => names.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (targets.Count == 0) return;

        using var timer = new PeriodicTimer(Interval);
        do
        {
            var client = factory.CreateClient("warmup");
            foreach (var service in targets)
            {
                try
                {
                    using var response = await client.GetAsync($"{service.Address}/health", stoppingToken);
                    logger.LogInformation("Keep-warm {Service}: {Status}", service.Name, (int)response.StatusCode);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Keep-warm {Service} failed: {Message}", service.Name, ex.Message);
                }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
