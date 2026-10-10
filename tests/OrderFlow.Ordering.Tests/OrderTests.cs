using OrderFlow.Contracts;
using OrderFlow.Ordering.API.Application.Commands;
using OrderFlow.Ordering.API.Domain;
using OrderFlow.ServiceDefaults;
using Shouldly;

namespace OrderFlow.Ordering.Tests;

public class OrderTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ShippingAddress Address = new("Dana Demo", "1 Main St", null, "Pune", "MH", "411001", "India");

    private static Order NewOrder(params (Guid, string, decimal, int)[] lines) =>
        Order.Create(Guid.NewGuid(), "dana@example.com", "Dana", "key-1", Address, PaymentScenarios.Approve, "4242",
            lines.Length > 0 ? lines : [(Guid.NewGuid(), "Widget", 10m, 2)], Now);

    [Fact]
    public void Create_computes_total_and_starts_pending()
    {
        var order = NewOrder((Guid.NewGuid(), "A", 10.50m, 2), (Guid.NewGuid(), "B", 5m, 1));

        order.Status.ShouldBe(OrderStatus.Pending);
        order.Total.ShouldBe(26.00m);
        order.Items.Count.ShouldBe(2);
        order.History.Single().Status.ShouldBe(OrderStatus.Pending);
        order.OrderNumber.ShouldStartWith("OF-260101-");
    }

    [Fact]
    public void Create_without_items_is_rejected()
    {
        Should.Throw<BusinessRuleException>(() =>
            Order.Create(Guid.NewGuid(), "e", "n", null, Address, PaymentScenarios.Approve, "4242", [], Now));
    }

    [Fact]
    public void Create_with_non_positive_quantity_is_rejected()
    {
        Should.Throw<BusinessRuleException>(() => NewOrder((Guid.NewGuid(), "A", 10m, 0)));
    }

    [Fact]
    public void Happy_path_moves_through_processing_to_completed()
    {
        var order = NewOrder();

        order.MarkPaymentProcessing(Now.AddSeconds(1)).ShouldBeTrue();
        order.MarkCompleted("txn_123", Now.AddSeconds(3)).ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Completed);
        order.TransactionReference.ShouldBe("txn_123");
        order.History.Select(h => h.Status).ShouldBe([OrderStatus.Pending, OrderStatus.PaymentProcessing, OrderStatus.Completed]);
    }

    [Fact]
    public void Transitions_are_idempotent_for_redelivered_events()
    {
        var order = NewOrder();
        order.MarkCompleted("txn_1", Now).ShouldBeTrue();

        order.MarkCompleted("txn_1", Now).ShouldBeFalse();
        order.MarkPaymentProcessing(Now).ShouldBeFalse();
        order.MarkFailed("late failure", Now).ShouldBeFalse();
        order.Status.ShouldBe(OrderStatus.Completed);
    }

    [Fact]
    public void Out_of_order_processing_event_after_completion_is_ignored()
    {
        var order = NewOrder();
        order.MarkCompleted("txn_1", Now);

        order.MarkPaymentProcessing(Now.AddSeconds(1)).ShouldBeFalse();
        order.History.Count.ShouldBe(2);
    }

    [Fact]
    public void Failure_records_reason()
    {
        var order = NewOrder();
        order.MarkFailed("Card declined", Now).ShouldBeTrue();

        order.Status.ShouldBe(OrderStatus.Failed);
        order.FailureReason.ShouldBe("Card declined");
    }
}

public class PlaceOrderValidatorTests
{
    private readonly PlaceOrderCommandValidator _validator = new();

    private static PlaceOrderCommand Valid() => new(
        Guid.NewGuid(), "dana@example.com", "Dana", "key",
        [new PlaceOrderItem(Guid.NewGuid(), 1)],
        new ShippingAddress("Dana", "1 Main St", null, "Pune", "MH", "411001", "India"),
        new PaymentDetails(PaymentScenarios.Approve, "4242"));

    [Fact]
    public void Valid_command_passes() => _validator.Validate(Valid()).IsValid.ShouldBeTrue();

    [Fact]
    public void Empty_cart_fails() =>
        _validator.Validate(Valid() with { Items = [] }).IsValid.ShouldBeFalse();

    [Fact]
    public void Unknown_payment_scenario_fails() =>
        _validator.Validate(Valid() with { Payment = new PaymentDetails("bitcoin", "4242") }).IsValid.ShouldBeFalse();

    [Fact]
    public void Missing_address_fails_without_throwing() =>
        _validator.Validate(Valid() with { ShippingAddress = null! }).IsValid.ShouldBeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Quantity_out_of_range_fails(int quantity) =>
        _validator.Validate(Valid() with { Items = [new PlaceOrderItem(Guid.NewGuid(), quantity)] }).IsValid.ShouldBeFalse();
}
