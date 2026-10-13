using System.Net.Http.Json;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Notification.Worker.Data;
using OrderFlow.Notification.Worker.Email;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Notification.Worker.Campaigns;

public sealed record MarketingRecipient(Guid UserId, string Email, string FullName, string UnsubscribeUrl);

/// <summary>Reads the opted-in customers from the Identity module (hosted by the gateway), with the internal key.</summary>
public sealed class RecipientsClient(HttpClient http, InternalApiKey key)
{
    public async Task<IReadOnlyList<MarketingRecipient>> GetAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/internal/marketing/recipients");
        request.Headers.Add(InternalApiKey.Header, key.Value);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<MarketingRecipient>>(ct) ?? [];
    }
}

/// <summary>
/// Emails a launched campaign to every opted-in customer, one personalised message each (their own
/// unsubscribe link). Each delivery is recorded before moving on, so a retried or redelivered event
/// resumes where it stopped and nobody gets the same email twice.
/// </summary>
public sealed class WeeklyDealsConsumer(
    NotificationDbContext db,
    RecipientsClient recipients,
    IEmailSender sender,
    EmailOptions options,
    ILogger<WeeklyDealsConsumer> logger) : IConsumer<WeeklyDealsLaunched>
{
    // Brevo's free plan allows 300 emails a day; leave room for anything else sent that day.
    private const int DailyCap = 280;

    public async Task Consume(ConsumeContext<WeeklyDealsLaunched> context)
    {
        var deal = context.Message;
        var ct = context.CancellationToken;

        var all = await recipients.GetAsync(ct);
        var done = (await db.CampaignDeliveries.AsNoTracking()
                .Where(d => d.CampaignId == deal.CampaignId)
                .Select(d => d.Recipient)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int sent = 0, failed = 0;
        foreach (var r in all.Where(r => !done.Contains(r.Email)).Take(DailyCap - done.Count))
        {
            var email = DealsEmail.Build(deal, r.Email, r.FullName, r.UnsubscribeUrl, options.FrontendUrl);
            var result = await sender.SendAsync(email, ct);

            db.CampaignDeliveries.Add(new CampaignDelivery
            {
                CampaignId = deal.CampaignId,
                Recipient = r.Email,
                Status = result.Status,
                ProviderMessageId = result.ProviderMessageId,
                Error = result.Error is { Length: > 1000 } e ? e[..1000] : result.Error
            });
            if (result.Delivered)
            {
                sent++;
                db.Notifications.Add(new NotificationMessage
                {
                    UserId = r.UserId, Recipient = r.Email, Subject = email.Subject,
                    Body = email.Text.Length > 4000 ? email.Text[..4000] : email.Text
                });
            }
            else
            {
                failed++;
            }
            await db.SaveChangesAsync(ct);
        }

        if (!await db.EventLogs.AnyAsync(e => e.EventId == deal.EventId, ct))
        {
            db.EventLogs.Add(new EventLogEntry
            {
                EventId = deal.EventId,
                EventType = nameof(WeeklyDealsLaunched),
                Source = "ordering",
                Summary = $"Weekly deals {deal.CouponCode}: {deal.PercentOff}% off {deal.Products.Count} products, " +
                          $"emailed to {done.Count + sent} customer(s){(failed > 0 ? $", {failed} rejected" : "")}" +
                          (options.IsLive ? "" : " (simulated: no BREVO_API_KEY)"),
                Severity = failed > 0 ? EventSeverity.Warning : EventSeverity.Success,
                Payload = JsonSerializer.Serialize(deal, JsonSerializerOptions.Web),
                OccurredAtUtc = deal.OccurredAtUtc
            });
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation("Campaign {Code}: {Sent} sent, {Failed} rejected, {Skipped} already sent",
            deal.CouponCode, sent, failed, done.Count);
    }
}
