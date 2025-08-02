using System.Text.Json;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ordering.Api.Domain;
using Ordering.Api.Orders;

namespace Ordering.Tests;

public sealed class BasketCheckoutHandlerTests : IDisposable
{
    private readonly OrderingDatabase _database = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task A_checkout_becomes_a_pending_order_and_starts_the_saga()
    {
        var keyboard = Guid.NewGuid();
        var checkout = new BasketCheckedOut(Guid.NewGuid(), "customer-1",
        [
            new CheckoutLine(keyboard, "Keyboard", 89.00m, "USD", 2),
            new CheckoutLine(Guid.NewGuid(), "Mouse", 39.50m, "USD", 1),
        ]);

        await HandleAsync(checkout);

        await using var db = _database.CreateContext();
        var order = await db.Orders.SingleAsync();
        Assert.Equal(checkout.OrderId, order.Id);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(217.50m, order.Total);
        var submitted = JsonSerializer.Deserialize<OrderSubmitted>(
            Assert.Single(_database.Outbox()).Payload, MessagingJson.Options)!;
        Assert.Equal(checkout.OrderId, submitted.OrderId);
        Assert.Equal(new OrderLineItem(keyboard, 2), submitted.Lines[0]);
    }

    [Fact]
    public async Task A_repeated_checkout_does_not_place_a_second_order()
    {
        var checkout = new BasketCheckedOut(Guid.NewGuid(), "customer-1", [new CheckoutLine(Guid.NewGuid(), "Keyboard", 89.00m, "USD", 1)]);

        await HandleAsync(checkout);
        await HandleAsync(checkout);

        await using var db = _database.CreateContext();
        Assert.Equal(1, await db.Orders.CountAsync());
        Assert.Single(_database.Outbox());
    }

    [Fact]
    public async Task A_checkout_that_breaks_an_order_invariant_is_consumed_without_an_order()
    {
        var mixedCurrencies = new BasketCheckedOut(Guid.NewGuid(), "customer-1",
        [
            new CheckoutLine(Guid.NewGuid(), "Keyboard", 89.00m, "USD", 1),
            new CheckoutLine(Guid.NewGuid(), "Mouse", 35.00m, "EUR", 1),
        ]);

        await HandleAsync(mixedCurrencies);

        await using var db = _database.CreateContext();
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.Set<OutboxMessage>().ToListAsync());
    }

    public void Dispose() => _database.Dispose();

    private async Task HandleAsync(BasketCheckedOut message)
    {
        await using var db = _database.CreateContext();
        var outbox = new EntityFrameworkOutbox<Ordering.Api.Data.OrderingDbContext>(db, new OutboxSignal(), _clock);
        var handler = new BasketCheckoutHandler(db, outbox, _clock, NullLogger<BasketCheckoutHandler>.Instance);
        await handler.HandleAsync(message, TestContext.For<BasketCheckedOut>(), CancellationToken.None);
        await db.SaveChangesAsync();
    }
}
