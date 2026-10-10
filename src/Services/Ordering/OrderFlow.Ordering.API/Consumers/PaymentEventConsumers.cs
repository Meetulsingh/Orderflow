using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Ordering.API.Application;
using OrderFlow.Ordering.API.Domain;
using OrderFlow.Ordering.API.Hubs;
using OrderFlow.Ordering.API.Infrastructure;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Ordering.API.Consumers;

/// <summary>
/// Applies a state transition to an order and pushes the result to connected clients.
/// Domain transitions are idempotent, and xmin optimistic concurrency + message retry
/// resolve races between events for the same order.
/// </summary>
public abstract class OrderTransitionConsumer<T>(OrderingDbContext db, OrderNotifier notifier, TimeProvider clock, ILogger logger)
    : IConsumer<T> where T : class
{
    protected abstract Guid GetOrderId(T message);
    protected abstract bool Apply(Order order, T message, DateTime nowUtc);

    public async Task Consume(ConsumeContext<T> context)
    {
        var orderId = GetOrderId(context.Message);
        var order = await db.Orders.AsSplitQuery().SingleOrDefaultAsync(o => o.Id == orderId, context.CancellationToken);
        if (order is null)
        {
            logger.LogWarning("Received {Event} for unknown order {OrderId}", typeof(T).Name, orderId);
            return;
        }

        if (!Apply(order, context.Message, clock.GetUtcNow().UtcDateTime))
        {
            logger.LogInformation("{Event} ignored for order {OrderNumber} in status {Status}", typeof(T).Name, order.OrderNumber, order.Status);
            return;
        }

        await db.SaveChangesAsync(context.CancellationToken);
        logger.LogInformation("Order {OrderNumber} → {Status}", order.OrderNumber, order.Status);
        await notifier.OrderUpdatedAsync(OrderDto.From(order), context.CancellationToken);
    }
}

public sealed class PaymentProcessingStartedConsumer(OrderingDbContext db, OrderNotifier notifier, TimeProvider clock, ILogger<PaymentProcessingStartedConsumer> logger)
    : OrderTransitionConsumer<PaymentProcessingStarted>(db, notifier, clock, logger)
{
    protected override Guid GetOrderId(PaymentProcessingStarted m) => m.OrderId;
    protected override bool Apply(Order order, PaymentProcessingStarted m, DateTime now) => order.MarkPaymentProcessing(now);
}

public sealed class PaymentSucceededConsumer(OrderingDbContext db, OrderNotifier notifier, TimeProvider clock, ILogger<PaymentSucceededConsumer> logger)
    : OrderTransitionConsumer<PaymentSucceeded>(db, notifier, clock, logger)
{
    protected override Guid GetOrderId(PaymentSucceeded m) => m.OrderId;
    protected override bool Apply(Order order, PaymentSucceeded m, DateTime now) => order.MarkCompleted(m.TransactionReference, now);
}

public sealed class PaymentFailedConsumer(OrderingDbContext db, OrderNotifier notifier, TimeProvider clock, ILogger<PaymentFailedConsumer> logger)
    : OrderTransitionConsumer<PaymentFailed>(db, notifier, clock, logger)
{
    protected override Guid GetOrderId(PaymentFailed m) => m.OrderId;
    protected override bool Apply(Order order, PaymentFailed m, DateTime now) => order.MarkFailed(m.Reason, now);
}

/// <summary>
/// Compensation: when the payment worker exhausts its retries the message is dead-lettered and MassTransit
/// publishes <see cref="Fault{T}"/>. The order is failed so the customer is never left waiting forever.
/// </summary>
public sealed class OrderSubmittedFaultConsumer(OrderingDbContext db, OrderNotifier notifier, TimeProvider clock, ILogger<OrderSubmittedFaultConsumer> logger)
    : OrderTransitionConsumer<Fault<OrderSubmitted>>(db, notifier, clock, logger)
{
    protected override Guid GetOrderId(Fault<OrderSubmitted> m) => m.Message.OrderId;

    protected override bool Apply(Order order, Fault<OrderSubmitted> m, DateTime now)
    {
        // Every subscriber of OrderSubmitted publishes its own fault; only the payment worker's failure
        // means the order can't be fulfilled (a notification hiccup must not cancel an order).
        if (m.FaultingService() != "payment") return false;

        var reason = m.Exceptions.FirstOrDefault()?.Message ?? "Payment service fault";
        return order.MarkFailed($"Payment service fault, message moved to dead-letter queue: {reason}", now);
    }
}
