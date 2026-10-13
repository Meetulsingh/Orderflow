using OrderFlow.Ordering.API.Domain;

namespace OrderFlow.Ordering.API.Application;

public sealed record OrderItemDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

public sealed record OrderStatusChangeDto(OrderStatus Status, string Note, DateTime OccurredAtUtc);

public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    Guid UserId,
    string CustomerName,
    string CustomerEmail,
    OrderStatus Status,
    decimal Subtotal,
    decimal Discount,
    string? CouponCode,
    decimal Total,
    string PaymentScenario,
    string CardLast4,
    string? TransactionReference,
    string? FailureReason,
    ShippingAddress ShippingAddress,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderStatusChangeDto> History,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static OrderDto From(Order o) => new(
        o.Id, o.OrderNumber, o.UserId, o.CustomerName, o.CustomerEmail, o.Status,
        o.Subtotal, o.Discount, o.CouponCode, o.Total,
        o.PaymentScenario, o.CardLast4, o.TransactionReference, o.FailureReason, o.ShippingAddress,
        o.Items.Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.LineTotal)).ToList(),
        o.History.OrderBy(h => h.OccurredAtUtc).Select(h => new OrderStatusChangeDto(h.Status, h.Note, h.OccurredAtUtc)).ToList(),
        o.CreatedAtUtc, o.UpdatedAtUtc);
}

public sealed record OrderStatsDto(
    int TotalOrders,
    int Pending,
    int PaymentProcessing,
    int Completed,
    int Failed,
    decimal Revenue,
    decimal AverageOrderValue,
    int OrdersLast24h);
