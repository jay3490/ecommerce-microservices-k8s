using System.Text.Json;
using Basket.Api.Baskets;
using Basket.Api.Domain;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;

namespace Basket.Tests;

public sealed class BasketCheckoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryBasketStore _store = new();
    private readonly FakePricing _pricing = new();
    private readonly FakePublisher _publisher = new();
    private readonly BasketCheckout _checkout;

    public BasketCheckoutTests() =>
        _checkout = new BasketCheckout(_store, _pricing, _publisher, NullLogger<BasketCheckout>.Instance);

    [Fact]
    public async Task Checkout_publishes_priced_lines_and_empties_the_basket()
    {
        var keyboard = _pricing.Add("Keyboard", 89.00m);
        var mouse = _pricing.Add("Mouse", 39.50m);
        await FillAsync((keyboard.Id, 2), (mouse.Id, 1));

        var result = await _checkout.CheckoutAsync("customer-1", CancellationToken.None);

        Assert.Equal(CheckoutStatus.Accepted, result.Status);
        var published = Assert.Single(_publisher.Messages);
        Assert.Equal(nameof(BasketCheckedOut), published.MessageType);
        var message = JsonSerializer.Deserialize<BasketCheckedOut>(published.Payload, MessagingJson.Options)!;
        Assert.Equal(result.OrderId, message.OrderId);
        Assert.Equal("customer-1", message.CustomerId);
        Assert.Equal(
            [
                new CheckoutLine(keyboard.Id, "Keyboard", 89.00m, "USD", 2),
                new CheckoutLine(mouse.Id, "Mouse", 39.50m, "USD", 1),
            ],
            message.Lines);
        Assert.Null(await _store.GetAsync("customer-1", CancellationToken.None));
    }

    [Fact]
    public async Task An_empty_or_missing_basket_is_not_checked_out()
    {
        var result = await _checkout.CheckoutAsync("customer-1", CancellationToken.None);

        Assert.Equal(CheckoutStatus.EmptyBasket, result.Status);
        Assert.Empty(_publisher.Messages);
    }

    [Fact]
    public async Task Products_the_catalog_does_not_know_are_reported_and_nothing_is_published()
    {
        var keyboard = _pricing.Add("Keyboard", 89.00m);
        var discontinued = Guid.NewGuid();
        await FillAsync((keyboard.Id, 1), (discontinued, 1));

        var result = await _checkout.CheckoutAsync("customer-1", CancellationToken.None);

        Assert.Equal(CheckoutStatus.UnknownProducts, result.Status);
        Assert.Equal([discontinued], result.UnknownProducts);
        Assert.Empty(_publisher.Messages);
        Assert.NotNull(await _store.GetAsync("customer-1", CancellationToken.None));
    }

    [Fact]
    public async Task A_catalog_outage_keeps_the_basket()
    {
        var keyboard = _pricing.Add("Keyboard", 89.00m);
        await FillAsync((keyboard.Id, 1));
        _pricing.Failure = new BrokenCircuitException();

        var result = await _checkout.CheckoutAsync("customer-1", CancellationToken.None);

        Assert.Equal(CheckoutStatus.CatalogUnavailable, result.Status);
        Assert.False((await _store.GetAsync("customer-1", CancellationToken.None))!.IsEmpty);
    }

    [Fact]
    public async Task A_retry_after_a_broker_failure_publishes_the_same_order_id()
    {
        var keyboard = _pricing.Add("Keyboard", 89.00m);
        await FillAsync((keyboard.Id, 1));
        _publisher.Fail = true;

        var failed = await _checkout.CheckoutAsync("customer-1", CancellationToken.None);
        _publisher.Fail = false;
        var retried = await _checkout.CheckoutAsync("customer-1", CancellationToken.None);

        Assert.Equal(CheckoutStatus.BrokerUnavailable, failed.Status);
        Assert.Equal(CheckoutStatus.Accepted, retried.Status);
        Assert.Equal(failed.OrderId, retried.OrderId);
    }

    [Fact]
    public async Task If_the_basket_cannot_be_deleted_a_second_checkout_repeats_the_same_order()
    {
        var keyboard = _pricing.Add("Keyboard", 89.00m);
        await FillAsync((keyboard.Id, 1));
        _store.FailDeletes = true;

        var first = await _checkout.CheckoutAsync("customer-1", CancellationToken.None);
        _store.FailDeletes = false;
        var second = await _checkout.CheckoutAsync("customer-1", CancellationToken.None);

        Assert.Equal(CheckoutStatus.Accepted, first.Status);
        Assert.Equal(2, _publisher.Messages.Count);

        var orderIds = _publisher.Messages
            .Select(m => JsonSerializer.Deserialize<BasketCheckedOut>(m.Payload, MessagingJson.Options)!.OrderId)
            .Distinct();
        Assert.Equal([second.OrderId!.Value], orderIds);
    }

    private async Task FillAsync(params (Guid ProductId, int Quantity)[] items)
    {
        var basket = new CustomerBasket("customer-1");
        foreach (var (productId, quantity) in items)
        {
            basket.SetQuantity(productId, quantity, Now);
        }

        await _store.SaveAsync(basket, CancellationToken.None);
    }
}
