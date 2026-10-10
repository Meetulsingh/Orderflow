using Microsoft.EntityFrameworkCore;
using OrderFlow.Payment.Worker;
using OrderFlow.Payment.Worker.Data;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// The worker exposes a tiny HTTP surface (health + admin read model) so it can run as a
// free-tier web service and be woken up by the gateway.
builder.AddServiceDefaults();
builder.AddPostgres<PaymentDbContext>(PaymentDbContext.Schema);
builder.AddMessaging("payment", x => x.AddConsumer<ProcessPaymentConsumer>());

var app = builder.Build();

app.UseServiceDefaults();

app.MapGet("/api/admin/payments", async (PaymentDbContext db, int? take, CancellationToken ct) =>
        await db.Payments.AsNoTracking()
            .OrderByDescending(p => p.ProcessedAtUtc)
            .Take(Math.Clamp(take ?? 50, 1, 200))
            .ToListAsync(ct))
    .RequireAuthorization(AppPolicies.Admin)
    .WithTags("Admin")
    .WithSummary("Recent payment attempts");

await app.MigrateDatabaseAsync<PaymentDbContext>();

app.Run();
