using BuildingBlocks.Messaging.Contracts;
using Ordering.Api.Domain;
using Ordering.Api.Saga;

namespace Ordering.Tests;

public sealed class OrderSagaTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly Guid Keyboard = Guid.NewGuid();
    private static readonly Guid Mouse = Guid.NewGuid();

    private readonly Order _order = Order.Place(
        Guid.NewGuid(),
        "customer-1",
        "USD",
        [
            new OrderItem(Keyboard, "Keyboard", 89.00m, "USD", 2),
            new OrderItem(Mouse, "Mouse", 39.50m, "USD", 1),
        ],
        Now);

    [Fact]
    public void Starting_the_saga_reserves_stock_for_every_line()
    {
        var (saga, step) = OrderSaga.Start(_order, Now);

        Assert.Equal(OrderSagaState.ReservingStock, saga.State);
        var command = Assert.IsType<ReserveStock>(Assert.Single(step.Messages));
        Assert.Equal(_order.Id, command.OrderId);
        Assert.Equal(
            [new OrderLineItem(Keyboard, 2), new OrderLineItem(Mouse, 1)],
            command.Lines);
    }

    [Fact]
    public void Only_pending_orders_can_start_a_saga()
    {
        _order.Cancel("Customer changed their mind", Now);

        Assert.Throws<InvalidOperationException>(() => OrderSaga.Start(_order, Now));
    }

    [Fact]
    public void Happy_path_reserves_stock_takes_payment_and_confirms_the_order()
    {
        var (saga, _) = OrderSaga.Start(_order, Now);
        var reservationId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var afterStock = saga.Handle(new StockReserved(_order.Id, reservationId), _order, Now.AddSeconds(1));

        Assert.Equal(OrderSagaState.ProcessingPayment, saga.State);
        Assert.Equal(reservationId, saga.ReservationId);
        Assert.Equal(
            new ProcessPayment(_order.Id, "customer-1", 217.50m, "USD"),
            Assert.Single(afterStock.Messages));

        var afterPayment = saga.Handle(new PaymentSucceeded(_order.Id, paymentId), _order, Now.AddSeconds(2));

        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.True(saga.IsFinished);
        Assert.Equal(paymentId, saga.PaymentId);
        Assert.Equal(OrderStatus.Confirmed, _order.Status);
        Assert.Equal(new OrderConfirmed(_order.Id), Assert.Single(afterPayment.Messages));
    }

    [Fact]
    public void Failing_to_reserve_stock_cancels_the_order_without_compensation()
    {
        var (saga, _) = OrderSaga.Start(_order, Now);

        var step = saga.Handle(new StockReservationFailed(_order.Id, "Insufficient stock for Keyboard"), _order, Now.AddSeconds(1));

        Assert.True(step.Applied);
        Assert.Empty(step.Messages);
        Assert.Equal(OrderSagaState.Cancelled, saga.State);
        Assert.Equal(OrderStatus.Cancelled, _order.Status);
        Assert.Equal("Insufficient stock for Keyboard", _order.CancellationReason);
    }

    [Fact]
    public void A_failed_payment_releases_the_reserved_stock_before_cancelling()
    {
        var (saga, _) = OrderSaga.Start(_order, Now);
        saga.Handle(new StockReserved(_order.Id, Guid.NewGuid()), _order, Now.AddSeconds(1));

        var compensation = saga.Handle(new PaymentFailed(_order.Id, "Card declined"), _order, Now.AddSeconds(2));

        Assert.Equal(OrderSagaState.ReleasingStock, saga.State);
        Assert.Equal(new ReleaseStock(_order.Id, "Card declined"), Assert.Single(compensation.Messages));
        Assert.Equal(OrderStatus.Pending, _order.Status);

        var released = saga.Handle(new StockReleased(_order.Id), _order, Now.AddSeconds(3));

        Assert.True(released.Applied);
        Assert.Empty(released.Messages);
        Assert.Equal(OrderSagaState.Cancelled, saga.State);
        Assert.Equal(OrderStatus.Cancelled, _order.Status);
        Assert.Equal("Card declined", _order.CancellationReason);
        Assert.Equal("Card declined", saga.FailureReason);
    }

    [Fact]
    public void A_payment_reply_before_the_stock_reply_is_ignored()
    {
        var (saga, _) = OrderSaga.Start(_order, Now);

        var step = saga.Handle(new PaymentSucceeded(_order.Id, Guid.NewGuid()), _order, Now.AddSeconds(1));

        Assert.False(step.Applied);
        Assert.Equal(OrderSagaState.ReservingStock, saga.State);
        Assert.Equal(OrderStatus.Pending, _order.Status);
        Assert.Equal(Now, saga.UpdatedAt);
    }

    [Fact]
    public void A_late_duplicate_reply_does_not_move_the_saga_backwards()
    {
        var (saga, _) = OrderSaga.Start(_order, Now);
        saga.Handle(new StockReserved(_order.Id, Guid.NewGuid()), _order, Now.AddSeconds(1));

        var duplicate = saga.Handle(new StockReserved(_order.Id, Guid.NewGuid()), _order, Now.AddSeconds(2));
        var stray = saga.Handle(new StockReleased(_order.Id), _order, Now.AddSeconds(2));

        Assert.False(duplicate.Applied);
        Assert.False(stray.Applied);
        Assert.Equal(OrderSagaState.ProcessingPayment, saga.State);
    }

    [Fact]
    public void A_finished_saga_ignores_every_message()
    {
        var (saga, _) = OrderSaga.Start(_order, Now);
        saga.Handle(new StockReserved(_order.Id, Guid.NewGuid()), _order, Now.AddSeconds(1));
        saga.Handle(new PaymentSucceeded(_order.Id, Guid.NewGuid()), _order, Now.AddSeconds(2));

        Assert.False(saga.Handle(new PaymentFailed(_order.Id, "late"), _order, Now.AddSeconds(3)).Applied);
        Assert.False(saga.Handle(new StockReservationFailed(_order.Id, "late"), _order, Now.AddSeconds(3)).Applied);
        Assert.False(saga.Handle(new StockReleased(_order.Id), _order, Now.AddSeconds(3)).Applied);
        Assert.Equal(OrderSagaState.Completed, saga.State);
        Assert.Equal(OrderStatus.Confirmed, _order.Status);
    }
}
