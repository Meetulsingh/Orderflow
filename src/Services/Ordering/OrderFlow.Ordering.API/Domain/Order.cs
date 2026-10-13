using System.Security.Cryptography;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Ordering.API.Domain;

public enum OrderStatus
{
    Pending,
    PaymentProcessing,
    Completed,
    Failed
}

public sealed record ShippingAddress(
    string FullName, string Line1, string? Line2, string City, string State, string PostalCode, string Country);

public sealed class OrderItem
{
    public Guid Id { get; private init; } = Guid.CreateVersion7();
    public Guid ProductId { get; private init; }
    public string ProductName { get; private init; } = string.Empty;
    public decimal UnitPrice { get; private init; }
    public int Quantity { get; private init; }
    public decimal LineTotal => UnitPrice * Quantity;

    private OrderItem() { }

    internal OrderItem(Guid productId, string productName, decimal unitPrice, int quantity)
    {
        if (quantity <= 0) throw new BusinessRuleException("Quantity must be at least 1.");
        if (unitPrice <= 0) throw new BusinessRuleException("Unit price must be positive.");
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }
}

public sealed class OrderStatusChange
{
    public Guid Id { get; private init; } = Guid.CreateVersion7();
    public OrderStatus Status { get; private init; }
    public string Note { get; private init; } = string.Empty;
    public DateTime OccurredAtUtc { get; private init; }

    private OrderStatusChange() { }

    internal OrderStatusChange(OrderStatus status, string note, DateTime at)
    {
        Status = status;
        Note = note;
        OccurredAtUtc = at;
    }
}

/// <summary>
/// Order aggregate root. All state transitions go through intention-revealing methods that
/// enforce the lifecycle: Pending → PaymentProcessing → Completed | Failed.
/// Transition methods are idempotent so redelivered events are harmless.
/// </summary>
public sealed class Order
{
    private readonly List<OrderItem> _items = [];
    private readonly List<OrderStatusChange> _history = [];

    public Guid Id { get; private init; } = Guid.CreateVersion7();
    public string OrderNumber { get; private init; } = string.Empty;
    public Guid UserId { get; private init; }
    public string CustomerEmail { get; private init; } = string.Empty;
    public string CustomerName { get; private init; } = string.Empty;
    public string? IdempotencyKey { get; private init; }
    public OrderStatus Status { get; private set; }
    public ShippingAddress ShippingAddress { get; private init; } = null!;
    public string PaymentScenario { get; private init; } = string.Empty;
    public string CardLast4 { get; private init; } = string.Empty;
    public string? TransactionReference { get; private set; }
    public string? FailureReason { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal Discount { get; private set; }
    public string? CouponCode { get; private init; }
    public decimal Total { get; private set; }
    public DateTime CreatedAtUtc { get; private init; }
    public DateTime UpdatedAtUtc { get; private set; }
    public uint Version { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;
    public IReadOnlyList<OrderStatusChange> History => _history;

    private Order() { }

    public static Order Create(
        Guid userId, string email, string name, string? idempotencyKey, ShippingAddress address,
        string paymentScenario, string cardLast4,
        IEnumerable<(Guid ProductId, string Name, decimal UnitPrice, int Quantity)> lines,
        DateTime nowUtc,
        Coupon? coupon = null)
    {
        if (coupon is not null && !coupon.IsActive(nowUtc))
            throw new BusinessRuleException($"Coupon {coupon.Code} has expired.");

        var order = new Order
        {
            OrderNumber = GenerateOrderNumber(nowUtc),
            UserId = userId,
            CustomerEmail = email,
            CustomerName = name,
            IdempotencyKey = idempotencyKey,
            ShippingAddress = address,
            PaymentScenario = paymentScenario,
            CardLast4 = cardLast4,
            CouponCode = coupon?.Code,
            Status = OrderStatus.Pending,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        foreach (var line in lines)
            order._items.Add(new OrderItem(line.ProductId, line.Name, line.UnitPrice, line.Quantity));

        if (order._items.Count == 0)
            throw new BusinessRuleException("An order must contain at least one item.");

        order.Subtotal = order._items.Sum(i => i.LineTotal);
        if (coupon is not null)
        {
            var eligible = order._items.Where(i => coupon.AppliesTo(i.ProductId)).ToList();
            if (eligible.Count == 0)
                throw new BusinessRuleException($"Coupon {coupon.Code} doesn't apply to any product in your cart.");
            order.Discount = eligible.Sum(i => (i.UnitPrice - coupon.DealPrice(i.UnitPrice)) * i.Quantity);
        }
        order.Total = order.Subtotal - order.Discount;
        order._history.Add(new OrderStatusChange(OrderStatus.Pending, "Order placed", nowUtc));
        return order;
    }

    /// <returns><c>true</c> if the state changed.</returns>
    public bool MarkPaymentProcessing(DateTime nowUtc)
    {
        if (Status != OrderStatus.Pending) return false;
        return Transition(OrderStatus.PaymentProcessing, "Payment authorisation started", nowUtc);
    }

    public bool MarkCompleted(string transactionReference, DateTime nowUtc)
    {
        if (Status is OrderStatus.Completed or OrderStatus.Failed) return false;
        TransactionReference = transactionReference;
        return Transition(OrderStatus.Completed, $"Payment captured ({transactionReference})", nowUtc);
    }

    public bool MarkFailed(string reason, DateTime nowUtc)
    {
        if (Status is OrderStatus.Completed or OrderStatus.Failed) return false;
        FailureReason = reason;
        return Transition(OrderStatus.Failed, reason, nowUtc);
    }

    private bool Transition(OrderStatus status, string note, DateTime nowUtc)
    {
        Status = status;
        UpdatedAtUtc = nowUtc;
        _history.Add(new OrderStatusChange(status, note, nowUtc));
        return true;
    }

    private static string GenerateOrderNumber(DateTime nowUtc) =>
        $"OF-{nowUtc:yyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3))}";
}
