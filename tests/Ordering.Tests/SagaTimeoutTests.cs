using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ordering.Api.Data;
using Ordering.Api.Domain;
using Ordering.Api.Saga;

namespace Ordering.Tests;

public sealed class SagaTimeoutTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly TimeSpan StepTimeout = TimeSpan.FromMinutes(5);

    private readonly OrderingDatabase _database = new();
    private readonly TestClock _clock = new(Start);

    [Fact]
    public void A_timeout_while_reserving_releases_whatever_might_have_been_reserved()
    {
        var (saga, order) = NewSaga();

        var step = saga.TimeOut(order, Start.AddMinutes(6));

        Assert.Equal(OrderSagaState.ReleasingStock, saga.State);
        Assert.Equal(1, saga.Timeouts);
        Assert.Equal(new ReleaseStock(order.Id, "Timed out waiting for the inventory service."), Assert.Single(step.Messages));
    }

    [Fact]
    public void A_timeout_while_paying_refunds_before_releasing()
    {
        var (saga, order) = NewSaga();
        saga.Handle(new StockReserved(order.Id, Guid.NewGuid()), order, Start);

        var step = saga.TimeOut(order, Start.AddMinutes(6));

        Assert.Equal(OrderSagaState.RefundingPayment, saga.State);
        var refund = Assert.IsType<RefundPayment>(Assert.Single(step.Messages));
        Assert.Equal(order.Total, refund.Amount);
        Assert.Equal("Timed out waiting for the payment service.", refund.Reason);
    }

    [Fact]
    public void A_timeout_while_shipping_cancels_the_shipment_and_refunds()
    {
        var (saga, order) = NewSaga();
        saga.Handle(new StockReserved(order.Id, Guid.NewGuid()), order, Start);
        saga.Handle(new PaymentSucceeded(order.Id, Guid.NewGuid()), order, Start);

        var step = saga.TimeOut(order, Start.AddMinutes(6));

        Assert.Equal(OrderSagaState.RefundingPayment, saga.State);
        Assert.Collection(step.Messages,
            m => Assert.Equal(new CancelShipment(order.Id, "Timed out waiting for the shipping service."), m),
            m => Assert.IsType<RefundPayment>(m));

        // The booking confirmation arriving after the timeout no longer confirms the order.
        Assert.False(saga.Handle(new ShipmentCreated(order.Id, Guid.NewGuid(), "SP1"), order, Start.AddMinutes(7)).Applied);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void An_unconfirmed_compensation_is_asked_for_again()
    {
        var (saga, order) = NewSaga();
        saga.TimeOut(order, Start.AddMinutes(6));

        var retry = saga.TimeOut(order, Start.AddMinutes(12));

        Assert.Equal(OrderSagaState.ReleasingStock, saga.State);
        Assert.Equal(2, saga.Timeouts);
        Assert.Equal(Start.AddMinutes(12), saga.UpdatedAt);
        Assert.IsType<ReleaseStock>(Assert.Single(retry.Messages));
    }

    [Fact]
    public void Finished_sagas_do_not_time_out()
    {
        var (saga, order) = NewSaga();
        saga.Handle(new StockReservationFailed(order.Id, "none left"), order, Start);

        Assert.False(saga.TimeOut(order, Start.AddHours(1)).Applied);
    }

    [Fact]
    public async Task The_processor_times_out_only_sagas_that_waited_longer_than_the_step_timeout()
    {
        var stuck = await StartSagaAsync();
        _clock.Advance(TimeSpan.FromMinutes(3));
        var recent = await StartSagaAsync();
        _clock.Advance(TimeSpan.FromMinutes(3));

        var processed = await ProcessAsync();

        Assert.Equal(1, processed);
        await using var db = _database.CreateContext();
        Assert.Equal(OrderSagaState.ReleasingStock, (await db.OrderSagas.SingleAsync(s => s.OrderId == stuck)).State);
        Assert.Equal(OrderSagaState.ReservingStock, (await db.OrderSagas.SingleAsync(s => s.OrderId == recent)).State);
        Assert.Contains(nameof(ReleaseStock), _database.OutboxTypes());
    }

    [Fact]
    public async Task A_timed_out_order_is_cancelled_once_the_compensation_is_confirmed()
    {
        var orderId = await StartSagaAsync();
        _clock.Advance(StepTimeout + TimeSpan.FromSeconds(1));
        await ProcessAsync();

        await using (var db = _database.CreateContext())
        {
            var handler = new OrderSagaHandler(db, Outbox(db), _clock, NullLogger<OrderSagaHandler>.Instance);
            await handler.HandleAsync(new StockReleased(orderId), TestContext.For<StockReleased>(), CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using var check = _database.CreateContext();
        var order = await check.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("Timed out waiting for the inventory service.", order.CancellationReason);
        Assert.Equal(nameof(OrderCancelled), _database.OutboxTypes().Last());
    }

    [Fact]
    public async Task Completed_sagas_are_left_alone()
    {
        var orderId = await StartSagaAsync();
        await using (var db = _database.CreateContext())
        {
            var handler = new OrderSagaHandler(db, Outbox(db), _clock, NullLogger<OrderSagaHandler>.Instance);
            await handler.HandleAsync(new StockReservationFailed(orderId, "none left"), TestContext.For<StockReservationFailed>(), CancellationToken.None);
            await db.SaveChangesAsync();
        }

        _clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, await ProcessAsync());
    }

    public void Dispose() => _database.Dispose();

    private (OrderSaga Saga, Order Order) NewSaga()
    {
        var order = Order.Place(Guid.NewGuid(), "customer-1", "USD", [new OrderItem(Guid.NewGuid(), "Keyboard", 89.00m, "USD", 1)], Start);
        var (saga, _) = OrderSaga.Start(order, Start);
        return (saga, order);
    }

    private async Task<Guid> StartSagaAsync()
    {
        var order = Order.Place(Guid.NewGuid(), "customer-1", "USD", [new OrderItem(Guid.NewGuid(), "Keyboard", 89.00m, "USD", 1)], _clock.Now);
        await using (var db = _database.CreateContext())
        {
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        await using (var db = _database.CreateContext())
        {
            var handler = new OrderSagaHandler(db, Outbox(db), _clock, NullLogger<OrderSagaHandler>.Instance);
            await handler.HandleAsync(
                new OrderSubmitted(order.Id, order.CustomerId, order.Total, order.Currency, []),
                TestContext.For<OrderSubmitted>(),
                CancellationToken.None);
            await db.SaveChangesAsync();
        }

        return order.Id;
    }

    private async Task<int> ProcessAsync()
    {
        await using var db = _database.CreateContext();
        var processor = new SagaTimeoutProcessor(db, Outbox(db), _clock,
            Options.Create(new SagaOptions { StepTimeout = StepTimeout }), NullLogger<SagaTimeoutProcessor>.Instance);
        return await processor.ProcessAsync(CancellationToken.None);
    }

    private EntityFrameworkOutbox<OrderingDbContext> Outbox(OrderingDbContext db) => new(db, new OutboxSignal(), _clock);
}
