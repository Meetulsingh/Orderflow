using Microsoft.EntityFrameworkCore;
using OrderFlow.Notification.Worker;
using OrderFlow.Notification.Worker.Data;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddPostgres<NotificationDbContext>(NotificationDbContext.Schema);
builder.AddMessaging("notification", x => x.AddConsumer<NotificationConsumer>());

var app = builder.Build();

app.UseServiceDefaults();

var admin = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization(AppPolicies.Admin);

admin.MapGet("/events", async (NotificationDbContext db, int? take, string? severity, CancellationToken ct) =>
{
    var query = db.EventLogs.AsNoTracking();
    if (Enum.TryParse<EventSeverity>(severity, ignoreCase: true, out var s))
        query = query.Where(e => e.Severity == s);
    return await query
        .OrderByDescending(e => e.OccurredAtUtc)
        .Take(Math.Clamp(take ?? 50, 1, 200))
        .Select(e => new { e.Id, e.EventId, e.EventType, e.Source, e.OrderNumber, e.Summary, e.Severity, e.Payload, e.OccurredAtUtc })
        .ToListAsync(ct);
}).WithSummary("Recent integration events observed on the bus");

admin.MapGet("/events/stats", async (NotificationDbContext db, CancellationToken ct) =>
{
    var since = DateTime.UtcNow.AddHours(-24);
    var byType = await db.EventLogs.AsNoTracking()
        .Where(e => e.OccurredAtUtc >= since)
        .GroupBy(e => e.EventType)
        .Select(g => new { eventType = g.Key, count = g.Count() })
        .OrderByDescending(x => x.count)
        .ToListAsync(ct);
    var deadLetters = await db.DeadLetters.CountAsync(ct);
    var emails = await db.Notifications.CountAsync(n => n.SentAtUtc >= since, ct);
    return new { last24h = byType, totalEvents24h = byType.Sum(x => x.count), deadLetters, emailsSent24h = emails };
}).WithSummary("Event throughput for the last 24 hours");

admin.MapGet("/dead-letters", async (NotificationDbContext db, CancellationToken ct) =>
        await db.DeadLetters.AsNoTracking().OrderByDescending(d => d.FaultedAtUtc).Take(50).ToListAsync(ct))
    .WithSummary("Messages moved to the dead-letter (error) queue");

admin.MapGet("/notifications", async (NotificationDbContext db, CancellationToken ct) =>
        await db.Notifications.AsNoTracking().OrderByDescending(n => n.SentAtUtc).Take(50).ToListAsync(ct))
    .WithSummary("Recently sent customer notifications");

app.MapGet("/api/notifications/mine", async (HttpContext http, NotificationDbContext db, CancellationToken ct) =>
    {
        var userId = http.User.GetUserId();
        return await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.SentAtUtc)
            .Take(20)
            .ToListAsync(ct);
    })
    .RequireAuthorization()
    .WithTags("Notifications")
    .WithSummary("Emails sent to the current user");

await app.MigrateDatabaseAsync<NotificationDbContext>();

app.Run();
