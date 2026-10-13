using System.Security.Cryptography;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Ordering.API.Domain;
using OrderFlow.Ordering.API.Infrastructure;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Ordering.API.Application;

public sealed record CouponDto(string Code, int PercentOff, IReadOnlyList<Guid> ProductIds, DateTime ExpiresAtUtc)
{
    public static CouponDto From(Coupon c) => new(c.Code, c.PercentOff, c.ProductIds, c.ExpiresAtUtc);
}

public sealed record LaunchCampaignRequest(string CampaignKey);

public sealed record LaunchCampaignResult(bool Created, CouponDto Coupon);

/// <summary>
/// Weekly deals: picks a few in-stock products, creates one time-limited coupon for them and publishes
/// <see cref="WeeklyDealsLaunched"/> through the outbox so the Notification service emails customers.
/// Idempotent per campaign key, so a retried scheduler call never sends a second email.
/// </summary>
public sealed class CampaignService(
    OrderingDbContext db,
    CatalogClient catalog,
    IPublishEndpoint publisher,
    TimeProvider clock,
    ILogger<CampaignService> logger)
{
    public const int DealCount = 3;
    public static readonly TimeSpan Duration = TimeSpan.FromHours(72);
    private static readonly int[] Percentages = [15, 20, 25, 30];

    public async Task<LaunchCampaignResult> LaunchAsync(string campaignKey, CancellationToken ct)
    {
        var existing = await db.Coupons.AsNoTracking().SingleOrDefaultAsync(c => c.CampaignKey == campaignKey, ct);
        if (existing is not null) return new LaunchCampaignResult(false, CouponDto.From(existing));

        var inStock = (await catalog.ListAsync(ct)).Where(p => p.Stock > 0).ToArray();
        if (inStock.Length == 0) throw new BusinessRuleException("No products are in stock, so there is nothing to put on sale.");

        RandomNumberGenerator.Shuffle(inStock.AsSpan());
        var picks = inStock.Take(DealCount).ToList();
        var percent = Percentages[RandomNumberGenerator.GetInt32(Percentages.Length)];
        var now = clock.GetUtcNow().UtcDateTime;
        var coupon = Coupon.Create(campaignKey, percent, picks.Select(p => p.Id).ToList(), now, Duration);

        db.Coupons.Add(coupon);
        await publisher.Publish(new WeeklyDealsLaunched(
            Guid.NewGuid(), now, coupon.Id, coupon.Code, coupon.PercentOff, coupon.ExpiresAtUtc,
            picks.Select(p => new DealProduct(p.Id, p.Name, p.Price, coupon.DealPrice(p.Price), p.ImageUrl)).ToList()), ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent trigger for the same campaign won the race; report its coupon instead.
            db.ChangeTracker.Clear();
            var winner = await db.Coupons.AsNoTracking().SingleAsync(c => c.CampaignKey == campaignKey, ct);
            return new LaunchCampaignResult(false, CouponDto.From(winner));
        }

        logger.LogInformation("Campaign {Key} launched coupon {Code}: {Percent}% off {Count} products until {Expires}",
            campaignKey, coupon.Code, percent, picks.Count, coupon.ExpiresAtUtc);
        return new LaunchCampaignResult(true, CouponDto.From(coupon));
    }
}

public static class CampaignEndpoints
{
    public static void MapCampaignEndpoints(this WebApplication app)
    {
        // Called by the gateway's scheduler endpoint (cron) or the admin "send now" button.
        app.MapPost("/api/internal/campaigns/weekly-deals", (LaunchCampaignRequest request, CampaignService service) =>
                // The scheduler may give up waiting, so don't let its disconnect abort a half-done launch.
                service.LaunchAsync(request.CampaignKey.Trim(), CancellationToken.None))
            .RequireInternalKey()
            .WithTags("Campaigns")
            .WithSummary("Launch the weekly deals campaign (idempotent per campaign key)");

        var coupons = app.MapGroup("/api/coupons").WithTags("Coupons");

        coupons.MapGet("/active", async (OrderingDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var coupon = await db.Coupons.AsNoTracking()
                .Where(c => c.StartsAtUtc <= now && c.ExpiresAtUtc > now)
                .OrderByDescending(c => c.StartsAtUtc)
                .FirstOrDefaultAsync(ct);
            return coupon is null ? Results.NoContent() : Results.Ok(CouponDto.From(coupon));
        }).WithSummary("This week's deal (public: the catalog shows its badges)");

        coupons.MapGet("/{code}", async (string code, OrderingDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var normalized = Coupon.Normalize(code);
            var coupon = await db.Coupons.AsNoTracking().SingleOrDefaultAsync(c => c.Code == normalized, ct)
                ?? throw new NotFoundException($"Coupon {normalized} doesn't exist.");
            if (!coupon.IsActive(clock.GetUtcNow().UtcDateTime))
                throw new BusinessRuleException($"Coupon {normalized} has expired.");
            return CouponDto.From(coupon);
        }).RequireAuthorization().WithSummary("Check a coupon code before checkout");
    }
}
