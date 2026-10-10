namespace OrderFlow.Contracts;

/// <summary>
/// Integration events exchanged between services over RabbitMQ.
/// Contracts are immutable records and live in a shared assembly so every
/// service serializes them to the same message URN.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTime OccurredAtUtc { get; }
}

public sealed record OrderLineItem(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);

/// <summary>Payment simulation scenarios selectable from the checkout UI.</summary>
public static class PaymentScenarios
{
    public const string Approve = "approve";
    public const string Decline = "decline";
    /// <summary>Consumer throws on every attempt so the message ends up in the error (dead-letter) queue.</summary>
    public const string Chaos = "chaos";

    public static readonly string[] All = [Approve, Decline, Chaos];
}

public sealed record OrderSubmitted(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid OrderId,
    string OrderNumber,
    Guid UserId,
    string CustomerEmail,
    string CustomerName,
    decimal Total,
    string PaymentScenario,
    string CardLast4,
    IReadOnlyList<OrderLineItem> Items) : IIntegrationEvent;

public sealed record PaymentProcessingStarted(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid OrderId,
    string OrderNumber,
    Guid UserId) : IIntegrationEvent;

public sealed record PaymentSucceeded(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid OrderId,
    string OrderNumber,
    Guid UserId,
    string CustomerEmail,
    decimal Amount,
    string TransactionReference,
    IReadOnlyList<OrderLineItem> Items) : IIntegrationEvent;

public sealed record PaymentFailed(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid OrderId,
    string OrderNumber,
    Guid UserId,
    string CustomerEmail,
    decimal Amount,
    string Reason) : IIntegrationEvent;

public sealed record UserRegistered(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid UserId,
    string Email,
    string FullName) : IIntegrationEvent;

public sealed record ProductStockLow(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid ProductId,
    string ProductName,
    int RemainingStock) : IIntegrationEvent;
