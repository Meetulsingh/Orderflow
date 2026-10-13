using OrderFlow.Contracts;
using OrderFlow.Ordering.API.Domain;
using OrderFlow.ServiceDefaults;
using Shouldly;

namespace OrderFlow.Ordering.Tests;

public class CouponTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 4, 30, 0, DateTimeKind.Utc);
    private static readonly ShippingAddress Address = new("Dana Demo", "1 Main St", null, "Pune", "MH", "411001", "India");
    private static readonly Guid OnSale = Guid.NewGuid();
    private static readonly Guid FullPrice = Guid.NewGuid();

    private static Coupon TwentyPercentOff() => Coupon.Create("weekly-2026-W42", 20, [OnSale], Now, TimeSpan.FromHours(72));

    private static Order Place(Coupon? coupon, DateTime at, params (Guid, string, decimal, int)[] lines) =>
        Order.Create(Guid.NewGuid(), "dana@example.com", "Dana", null, Address, PaymentScenarios.Approve, "4242", lines, at, coupon);

    [Fact]
    public void Discount_applies_only_to_products_in_the_deal()
    {
        var order = Place(TwentyPercentOff(), Now.AddHours(1), (OnSale, "Headphones", 179m, 2), (FullPrice, "Lamp", 69m, 1));

        order.Subtotal.ShouldBe(427m);
        order.Discount.ShouldBe(71.60m);   // 20% of 2 × 179, not of the lamp
        order.Total.ShouldBe(355.40m);
        order.CouponCode.ShouldStartWith("DEALS-");
    }

    [Fact]
    public void Deal_price_rounds_to_cents()
    {
        Coupon.Create("k", 15, [OnSale], Now, TimeSpan.FromHours(1)).DealPrice(44m).ShouldBe(37.40m);
        Coupon.Create("k", 30, [OnSale], Now, TimeSpan.FromHours(1)).DealPrice(0.99m).ShouldBe(0.69m);
    }

    [Fact]
    public void Expired_coupon_is_rejected()
    {
        Should.Throw<BusinessRuleException>(() => Place(TwentyPercentOff(), Now.AddHours(73), (OnSale, "Headphones", 179m, 1)))
            .Message.ShouldContain("expired");
    }

    [Fact]
    public void Coupon_that_matches_nothing_in_the_cart_is_rejected()
    {
        Should.Throw<BusinessRuleException>(() => Place(TwentyPercentOff(), Now, (FullPrice, "Lamp", 69m, 1)))
            .Message.ShouldContain("doesn't apply");
    }

    [Fact]
    public void Order_without_coupon_has_no_discount()
    {
        var order = Place(null, Now, (FullPrice, "Lamp", 69m, 2));
        order.Subtotal.ShouldBe(138m);
        order.Discount.ShouldBe(0m);
        order.Total.ShouldBe(138m);
        order.CouponCode.ShouldBeNull();
    }

    [Fact]
    public void Codes_are_normalized_and_easy_to_type()
    {
        Coupon.Normalize("  deals-ab2cd ").ShouldBe("DEALS-AB2CD");
        TwentyPercentOff().Code.ShouldMatch("^DEALS-[A-HJ-NP-Z2-9]{5}$");
    }
}
