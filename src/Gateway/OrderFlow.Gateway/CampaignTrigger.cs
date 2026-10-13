using System.Globalization;
using System.Net.Http.Json;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Gateway;

/// <summary>
/// Starts the weekly deals campaign. Called every Monday by an external scheduler (cron-job.org, with the
/// internal key) or on demand by an admin. It answers at once and does the work in the background, because
/// on free hosting Ordering and Notification may need a minute to wake up and a scheduler won't wait that long.
/// </summary>
public static class CampaignTrigger
{
    // India Standard Time has no daylight saving, and the chiseled runtime image ships no time-zone database.
    private static readonly TimeSpan IndiaOffset = TimeSpan.FromMinutes(330);

    public static void MapCampaignTrigger(this WebApplication app, IReadOnlyList<DownstreamService> services)
    {
        app.MapPost("/api/system/campaigns/weekly-deals", (HttpContext http, InternalApiKey key, IHttpClientFactory factory, ILoggerFactory loggers) =>
            {
                var fromScheduler = key.Matches(http.Request.Headers[InternalApiKey.Header].FirstOrDefault());
                if (!fromScheduler && !http.User.IsAdmin())
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized",
                        detail: "Only the scheduler or an admin can start a campaign.");

                // One scheduled campaign per ISO week (a retried cron call is a no-op); every admin click is a new one.
                var campaignKey = fromScheduler ? WeekKey(DateTime.UtcNow) : $"manual-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                var logger = loggers.CreateLogger("Campaigns");
                _ = Task.Run(() => LaunchAsync(factory, services, key, campaignKey, logger));
                return Results.Accepted(value: new { campaignKey, status = "started" });
            })
            .WithTags("System")
            .WithSummary("Start the weekly deals campaign (scheduler with the internal key, or an admin)");
    }

    public static string WeekKey(DateTime utcNow)
    {
        var local = utcNow + IndiaOffset;
        return $"weekly-{ISOWeek.GetYear(local)}-W{ISOWeek.GetWeekOfYear(local):00}";
    }

    private static async Task LaunchAsync(IHttpClientFactory factory, IReadOnlyList<DownstreamService> services,
        InternalApiKey key, string campaignKey, ILogger logger)
    {
        var client = factory.CreateClient("warmup");
        var ordering = services.Single(s => s.Name == "ordering");

        // Wake the services the campaign flows through, so the email goes out as soon as the coupon exists.
        foreach (var service in services.Where(s => s.Name is "catalog" or "notification"))
            _ = client.GetAsync($"{service.Address}/health").ContinueWith(_ => { }, TaskScheduler.Default);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{ordering.Address}/api/internal/campaigns/weekly-deals")
                {
                    Content = JsonContent.Create(new { campaignKey })
                };
                request.Headers.Add(InternalApiKey.Header, key.Value);
                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Campaign {Key} launched: {Body}", campaignKey, body);
                    return;
                }
                logger.LogWarning("Campaign {Key} attempt {Attempt} got {Status}: {Body}", campaignKey, attempt, (int)response.StatusCode, body);
                if ((int)response.StatusCode < 500) return; // a business error (e.g. nothing in stock) won't fix itself
            }
            catch (Exception ex)
            {
                logger.LogWarning("Campaign {Key} attempt {Attempt} failed: {Message}", campaignKey, attempt, ex.Message);
            }
            await Task.Delay(TimeSpan.FromSeconds(15));
        }
        logger.LogError("Campaign {Key} could not be launched", campaignKey);
    }
}
