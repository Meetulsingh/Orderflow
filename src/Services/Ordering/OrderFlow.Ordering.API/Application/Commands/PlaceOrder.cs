using FluentValidation;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Contracts;
using OrderFlow.Ordering.API.Domain;
using OrderFlow.Ordering.API.Infrastructure;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Ordering.API.Application.Commands;

public sealed record PlaceOrderItem(Guid ProductId, int Quantity);

public sealed record PaymentDetails(string Scenario, string CardLast4);

public sealed record PlaceOrderRequest(
    IReadOnlyList<PlaceOrderItem> Items,
    ShippingAddress ShippingAddress,
    PaymentDetails Payment,
    string? CouponCode = null);

public sealed record PlaceOrderCommand(
    Guid UserId,
    string CustomerEmail,
    string CustomerName,
    string? IdempotencyKey,
    IReadOnlyList<PlaceOrderItem> Items,
    ShippingAddress ShippingAddress,
    PaymentDetails Payment,
    string? CouponCode = null) : IRequest<OrderDto>;

public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CustomerEmail).NotEmpty();
        RuleFor(x => x.IdempotencyKey).MaximumLength(64);
        RuleFor(x => x.CouponCode).MaximumLength(32);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Your cart is empty.")
            .Must(items => items.Count <= 50).WithMessage("An order can contain at most 50 lines.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, 20);
        });

        RuleFor(x => x.ShippingAddress).NotNull().SetValidator(new ShippingAddressValidator());
        RuleFor(x => x.Payment).NotNull().SetValidator(new PaymentDetailsValidator());
    }
}

public sealed class ShippingAddressValidator : AbstractValidator<ShippingAddress>
{
    public ShippingAddressValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Line2).MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(60);
    }
}

public sealed class PaymentDetailsValidator : AbstractValidator<PaymentDetails>
{
    public PaymentDetailsValidator()
    {
        RuleFor(x => x.Scenario).Must(s => PaymentScenarios.All.Contains(s)).WithMessage("Unknown payment scenario.");
        RuleFor(x => x.CardLast4).Matches("^[0-9]{4}$").WithMessage("Card must end in 4 digits.");
    }
}

/// <summary>
/// Write side of CQRS: validates stock and prices against the Catalog service, persists the order and
/// stores <see cref="OrderSubmitted"/> in the transactional outbox in the same database transaction.
/// The HTTP request returns immediately; payment happens asynchronously.
/// </summary>
public sealed class PlaceOrderHandler(
    OrderingDbContext db,
    CatalogClient catalog,
    IPublishEndpoint publisher,
    TimeProvider clock,
    ILogger<PlaceOrderHandler> logger) : IRequestHandler<PlaceOrderCommand, OrderDto>
{
    public async Task<OrderDto> Handle(PlaceOrderCommand command, CancellationToken ct)
    {
        if (command.IdempotencyKey is not null)
        {
            var existing = await FindByIdempotencyKey(command.UserId, command.IdempotencyKey, ct);
            if (existing is not null) return OrderDto.From(existing);
        }

        var quantities = command.Items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        IReadOnlyList<CatalogProduct> products;
        try
        {
            products = await catalog.LookupAsync(quantities.Keys, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            logger.LogError(ex, "Catalog lookup failed");
            throw new BusinessRuleException("The catalog service is temporarily unavailable. Please try again in a moment.");
        }

        var lines = new List<(Guid, string, decimal, int)>();
        foreach (var (productId, quantity) in quantities)
        {
            var product = products.FirstOrDefault(p => p.Id == productId)
                ?? throw new BusinessRuleException("One of the products in your cart is no longer available.");
            if (product.Stock < quantity)
                throw new BusinessRuleException($"Only {product.Stock} unit(s) of \"{product.Name}\" left in stock.");
            lines.Add((product.Id, product.Name, product.Price, quantity));
        }

        var now = clock.GetUtcNow().UtcDateTime;

        // Prices and the discount are always computed here from authoritative data, never taken from the client.
        Coupon? coupon = null;
        if (!string.IsNullOrWhiteSpace(command.CouponCode))
        {
            var code = Coupon.Normalize(command.CouponCode);
            coupon = await db.Coupons.AsNoTracking().SingleOrDefaultAsync(c => c.Code == code, ct)
                ?? throw new BusinessRuleException($"Coupon {code} doesn't exist.");
        }

        var order = Order.Create(command.UserId, command.CustomerEmail, command.CustomerName, command.IdempotencyKey,
            command.ShippingAddress, command.Payment.Scenario, command.Payment.CardLast4, lines, now, coupon);

        db.Orders.Add(order);
        await publisher.Publish(new OrderSubmitted(
            Guid.NewGuid(), now, order.Id, order.OrderNumber, order.UserId, order.CustomerEmail, order.CustomerName,
            order.Total, order.PaymentScenario, order.CardLast4,
            order.Items.Select(i => new OrderLineItem(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity)).ToList()), ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (command.IdempotencyKey is not null)
        {
            // A concurrent duplicate submission won the race; return the order it created.
            db.ChangeTracker.Clear();
            var existing = await FindByIdempotencyKey(command.UserId, command.IdempotencyKey, ct);
            if (existing is not null) return OrderDto.From(existing);
            throw;
        }

        logger.LogInformation("Order {OrderNumber} placed by {UserId} for {Total}", order.OrderNumber, order.UserId, order.Total);
        return OrderDto.From(order);
    }

    private Task<Order?> FindByIdempotencyKey(Guid userId, string key, CancellationToken ct) =>
        db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.UserId == userId && o.IdempotencyKey == key, ct);
}
