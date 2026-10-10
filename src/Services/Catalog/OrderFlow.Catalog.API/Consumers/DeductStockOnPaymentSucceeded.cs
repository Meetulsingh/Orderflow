using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Catalog.API.Data;
using OrderFlow.Catalog.API.Products;
using OrderFlow.Contracts;

namespace OrderFlow.Catalog.API.Consumers;

/// <summary>
/// Deducts stock once an order is paid. Idempotent: the processed-order row and the stock updates
/// commit in one transaction, so a redelivered event becomes a no-op.
/// </summary>
public sealed class DeductStockOnPaymentSucceeded(
    CatalogDbContext db,
    ProductCache cache,
    ILogger<DeductStockOnPaymentSucceeded> logger) : IConsumer<PaymentSucceeded>
{
    private const int LowStockThreshold = 5;

    public async Task Consume(ConsumeContext<PaymentSucceeded> context)
    {
        var message = context.Message;
        var ct = context.CancellationToken;

        if (await db.ProcessedOrders.AnyAsync(p => p.OrderId == message.OrderId, ct))
        {
            logger.LogInformation("Stock already deducted for order {OrderNumber}; skipping", message.OrderNumber);
            return;
        }

        var quantities = message.Items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        var products = await db.Products.Where(p => quantities.Keys.Contains(p.Id)).ToListAsync(ct);

        foreach (var product in products)
        {
            product.Stock = Math.Max(0, product.Stock - quantities[product.Id]);
            product.UpdatedAtUtc = DateTime.UtcNow;
        }
        db.ProcessedOrders.Add(new ProcessedOrder { OrderId = message.OrderId });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.ProcessedOrders.AsNoTracking().AnyAsync(p => p.OrderId == message.OrderId, ct)) throw;
            logger.LogInformation("Concurrent duplicate for order {OrderNumber}; skipping", message.OrderNumber);
            return;
        }

        await cache.InvalidateAsync(ct);
        logger.LogInformation("Deducted stock for order {OrderNumber} ({Count} products)", message.OrderNumber, products.Count);

        foreach (var product in products.Where(p => p.Stock <= LowStockThreshold))
        {
            await context.Publish(new ProductStockLow(Guid.NewGuid(), DateTime.UtcNow, product.Id, product.Name, product.Stock), ct);
        }
    }
}
