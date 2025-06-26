using Ordering.Api.Domain;

namespace Ordering.Tests;

public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly Guid Keyboard = Guid.NewGuid();
    private static readonly Guid Mouse = Guid.NewGuid();

    [Fact]
    public void A_placed_order_is_pending_and_totals_its_lines()
    {
        var order = Place(Item(Keyboard, 89.00m, 2), Item(Mouse, 39.50m, 1));

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(2, order.Lines.Count);
        Assert.Equal(217.50m, order.Total);
        Assert.Equal("USD", order.Currency);
        Assert.Equal(Now, order.CreatedAt);
    }

    [Fact]
    public void The_same_product_twice_is_merged_into_one_line()
    {
        var order = Place(Item(Keyboard, 89.00m, 2), Item(Keyboard, 89.00m, 3));

        var line = Assert.Single(order.Lines);
        Assert.Equal(5, line.Quantity);
        Assert.Equal(445.00m, order.Total);
    }

    [Fact]
    public void An_order_needs_at_least_one_line() =>
        Assert.Throws<OrderingDomainException>(() => Place());

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Order.MaxQuantityPerLine + 1)]
    public void Quantities_must_be_within_bounds(int quantity) =>
        Assert.Throws<OrderingDomainException>(() => Place(Item(Keyboard, 89.00m, quantity)));

    [Fact]
    public void Merging_lines_cannot_exceed_the_quantity_limit() =>
        Assert.Throws<OrderingDomainException>(() =>
            Place(Item(Keyboard, 89.00m, Order.MaxQuantityPerLine), Item(Keyboard, 89.00m, 1)));

    [Fact]
    public void The_same_product_cannot_have_two_prices() =>
        Assert.Throws<OrderingDomainException>(() => Place(Item(Keyboard, 89.00m, 1), Item(Keyboard, 79.00m, 1)));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Prices_must_be_positive(decimal price) =>
        Assert.Throws<OrderingDomainException>(() => Place(Item(Keyboard, price, 1)));

    [Fact]
    public void All_lines_must_be_in_the_order_currency() =>
        Assert.Throws<OrderingDomainException>(() => Place(Item(Keyboard, 89.00m, 1), Item(Mouse, 39.50m, 1, "EUR")));

    [Fact]
    public void An_order_has_a_maximum_number_of_lines()
    {
        var items = Enumerable.Range(0, Order.MaxLines + 1).Select(_ => Item(Guid.NewGuid(), 1m, 1)).ToArray();

        Assert.Throws<OrderingDomainException>(() => Place(items));
        Assert.Equal(Order.MaxLines, Place(items[..Order.MaxLines]).Lines.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void An_order_needs_a_customer(string customerId) =>
        Assert.Throws<OrderingDomainException>(() =>
            Order.Place(Guid.NewGuid(), customerId, "USD", [Item(Keyboard, 1m, 1)], Now));

    [Theory]
    [InlineData("US")]
    [InlineData("US1")]
    public void The_currency_must_be_an_iso_code(string currency) =>
        Assert.Throws<OrderingDomainException>(() =>
            Order.Place(Guid.NewGuid(), "customer-1", currency, [Item(Keyboard, 1m, 1, currency)], Now));

    [Fact]
    public void Confirming_a_pending_order_is_final_and_idempotent()
    {
        var order = Place(Item(Keyboard, 89.00m, 1));

        order.Confirm(Now.AddMinutes(1));
        order.Confirm(Now.AddMinutes(2));

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(Now.AddMinutes(1), order.UpdatedAt);
        Assert.Throws<OrderingDomainException>(() => order.Cancel("too late", Now.AddMinutes(3)));
    }

    [Fact]
    public void Cancelling_a_pending_order_is_final_and_keeps_the_first_reason()
    {
        var order = Place(Item(Keyboard, 89.00m, 1));

        order.Cancel("Payment declined", Now.AddMinutes(1));
        order.Cancel("Something else", Now.AddMinutes(2));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("Payment declined", order.CancellationReason);
        Assert.Throws<OrderingDomainException>(() => order.Confirm(Now.AddMinutes(3)));
    }

    private static Order Place(params OrderItem[] items) =>
        Order.Place(Guid.NewGuid(), "customer-1", "USD", items, Now);

    private static OrderItem Item(Guid productId, decimal price, int quantity, string currency = "USD") =>
        new(productId, $"Product {productId:N}", price, currency, quantity);
}
