using System.Security.Cryptography;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Payment.Worker.Data;

namespace OrderFlow.Payment.Worker;

/// <summary>
/// Simulated payment gateway. Runs entirely off the user's HTTP request thread.
/// <list type="bullet">
/// <item><c>approve</c> — payment captured, <see cref="PaymentSucceeded"/> published.</item>
/// <item><c>decline</c> — issuer decline, <see cref="PaymentFailed"/> published.</item>
/// <item><c>chaos</c> — the gateway "crashes" on every attempt; after retries the message is dead-lettered.</item>
/// </list>
/// </summary>
public sealed class ProcessPaymentConsumer(
    PaymentDbContext db,
    IConfiguration configuration,
    ILogger<ProcessPaymentConsumer> logger) : IConsumer<OrderSubmitted>
{
    public async Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        var order = context.Message;
        var ct = context.CancellationToken;

        // Idempotency: if this order was already charged, re-publish the outcome instead of charging twice.
        var existing = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.OrderId == order.OrderId, ct);
        if (existing is not null)
        {
            logger.LogInformation("Payment for {OrderNumber} already processed ({Status}); re-publishing result", order.OrderNumber, existing.Status);
            await PublishOutcome(context, existing);
            return;
        }

        await context.Publish(new PaymentProcessingStarted(Guid.NewGuid(), DateTime.UtcNow, order.OrderId, order.OrderNumber, order.UserId), ct);

        if (order.PaymentScenario == PaymentScenarios.Chaos)
        {
            logger.LogWarning("Simulated gateway outage for {OrderNumber} (attempt {Attempt})", order.OrderNumber, context.GetRetryAttempt() + 1);
            throw new InvalidOperationException("Simulated payment gateway outage (HTTP 503 from acquirer).");
        }

        // Simulate the latency of a real card authorisation so the live status pipeline is visible in the UI.
        var delayMs = configuration.GetValue("Payment:SimulatedLatencyMs", 2500);
        await Task.Delay(delayMs, ct);

        var approved = order.PaymentScenario == PaymentScenarios.Approve;
        var record = new PaymentRecord
        {
            OrderId = order.OrderId,
            OrderNumber = order.OrderNumber,
            UserId = order.UserId,
            CustomerEmail = order.CustomerEmail,
            Amount = order.Total,
            CardLast4 = order.CardLast4,
            Status = approved ? PaymentStatus.Succeeded : PaymentStatus.Declined,
            TransactionReference = approved ? $"txn_{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}" : null,
            FailureReason = approved ? null : "Card declined by issuer (insufficient funds)."
        };

        db.Payments.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a redelivered copy of the same message — publish whatever won.
            db.ChangeTracker.Clear();
            record = await db.Payments.AsNoTracking().SingleAsync(p => p.OrderId == order.OrderId, ct);
        }

        logger.LogInformation("Payment for {OrderNumber}: {Status} {Amount}", order.OrderNumber, record.Status, record.Amount);
        await PublishOutcome(context, record, order.Items);
    }

    private static Task PublishOutcome(ConsumeContext<OrderSubmitted> context, PaymentRecord payment, IReadOnlyList<OrderLineItem>? items = null)
    {
        var m = context.Message;
        return payment.Status == PaymentStatus.Succeeded
            ? context.Publish(new PaymentSucceeded(Guid.NewGuid(), DateTime.UtcNow, m.OrderId, m.OrderNumber, m.UserId,
                m.CustomerEmail, payment.Amount, payment.TransactionReference!, items ?? m.Items), context.CancellationToken)
            : context.Publish(new PaymentFailed(Guid.NewGuid(), DateTime.UtcNow, m.OrderId, m.OrderNumber, m.UserId,
                m.CustomerEmail, payment.Amount, payment.FailureReason ?? "Payment declined."), context.CancellationToken);
    }
}
