using Basket.Api.Domain;

namespace Basket.Tests;

public sealed class CustomerBasketTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);
    private readonly CustomerBasket _basket = new("customer-1");

    [Fact]
    public void Setting_a_quantity_adds_updates_and_removes_items()
    {
        var keyboard = Guid.NewGuid();

        _basket.SetQuantity(keyboard, 2, Now);
        _basket.SetQuantity(keyboard, 5, Now.AddMinutes(1));

        Assert.Equal([new BasketItem(keyboard, 5)], _basket.Items);
        Assert.Equal(Now.AddMinutes(1), _basket.UpdatedAt);

        _basket.SetQuantity(keyboard, 0, Now.AddMinutes(2));
        Assert.True(_basket.IsEmpty);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(CustomerBasket.MaxQuantity + 1)]
    public void Quantities_outside_the_limits_are_rejected(int quantity)
    {
        Assert.Throws<BasketDomainException>(() => _basket.SetQuantity(Guid.NewGuid(), quantity, Now));
    }

    [Fact]
    public void A_basket_holds_a_limited_number_of_products()
    {
        for (var i = 0; i < CustomerBasket.MaxItems; i++)
        {
            _basket.SetQuantity(Guid.NewGuid(), 1, Now);
        }

        Assert.Throws<BasketDomainException>(() => _basket.SetQuantity(Guid.NewGuid(), 1, Now));
    }

    [Fact]
    public void Retrying_a_checkout_reuses_the_reserved_order_id()
    {
        _basket.SetQuantity(Guid.NewGuid(), 1, Now);

        var first = _basket.BeginCheckout();
        var second = _basket.BeginCheckout();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Changing_the_basket_after_a_failed_checkout_starts_a_new_order()
    {
        var keyboard = Guid.NewGuid();
        _basket.SetQuantity(keyboard, 1, Now);
        var first = _basket.BeginCheckout();

        _basket.SetQuantity(keyboard, 2, Now);

        Assert.Null(_basket.PendingCheckoutId);
        Assert.NotEqual(first, _basket.BeginCheckout());
    }

    [Fact]
    public void An_empty_basket_cannot_be_checked_out()
    {
        Assert.Throws<BasketDomainException>(() => _basket.BeginCheckout());
    }
}
