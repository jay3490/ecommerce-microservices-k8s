using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using Ordering.Api.Domain;

namespace Ordering.Api.Saga;

public enum OrderSagaState
{
    /// <summary>ReserveStock sent, waiting for the inventory service.</summary>
    ReservingStock,

    /// <summary>Stock is held, ProcessPayment sent, waiting for the payment service.</summary>
    ProcessingPayment,

    /// <summary>Payment failed; compensating by asking inventory to release the stock.</summary>
    ReleasingStock,

    /// <summary>Paid and confirmed.</summary>
    Completed,

    /// <summary>Stopped by a failure, with any reserved stock released.</summary>
    Cancelled,
}

/// <summary>The outcome of feeding one message to the saga.</summary>
public sealed record SagaStep(bool Applied, IReadOnlyList<IIntegrationMessage> Messages)
{
    public static readonly SagaStep Ignored = new(false, []);

    public static SagaStep Then(params IIntegrationMessage[] messages) => new(true, messages);
}

/// <summary>
/// Orchestrates one order: reserve stock, take payment, confirm; on a payment failure it
/// releases the reservation before cancelling. The state is persisted after every step, so
/// the saga resumes correctly after a restart. Each method accepts a message only in the
/// state that expects it; anything else (a late duplicate, a reply for an older step) is
/// ignored and leaves the saga unchanged.
/// </summary>
//
//  ReservingStock --StockReserved--> ProcessingPayment --PaymentSucceeded--> Completed
//        |                                  |
//   StockReservationFailed            PaymentFailed
//        v                                  v
//    Cancelled <-------StockReleased---- ReleasingStock
//
public sealed class OrderSaga
{
    private OrderSaga()
    {
    }

    public Guid OrderId { get; private set; }
    public OrderSagaState State { get; private set; }
    public Guid? ReservationId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsFinished => State is OrderSagaState.Completed or OrderSagaState.Cancelled;

    public static (OrderSaga Saga, SagaStep Step) Start(Order order, DateTimeOffset now)
    {
        if (order.Status != OrderStatus.Pending)
        {
            throw new InvalidOperationException($"Order {order.Id} is {order.Status}; only pending orders start a saga.");
        }

        var saga = new OrderSaga
        {
            OrderId = order.Id,
            State = OrderSagaState.ReservingStock,
            StartedAt = now,
            UpdatedAt = now,
        };

        var lines = order.Lines.Select(line => new OrderLineItem(line.ProductId, line.Quantity)).ToList();
        return (saga, SagaStep.Then(new ReserveStock(order.Id, lines)));
    }

    public SagaStep Handle(StockReserved message, Order order, DateTimeOffset now)
    {
        if (State != OrderSagaState.ReservingStock)
        {
            return SagaStep.Ignored;
        }

        ReservationId = message.ReservationId;
        MoveTo(OrderSagaState.ProcessingPayment, now);
        return SagaStep.Then(new ProcessPayment(order.Id, order.CustomerId, order.Total, order.Currency));
    }

    public SagaStep Handle(StockReservationFailed message, Order order, DateTimeOffset now)
    {
        if (State != OrderSagaState.ReservingStock)
        {
            return SagaStep.Ignored;
        }

        // Nothing was reserved, so there is nothing to compensate.
        FailureReason = message.Reason;
        order.Cancel(message.Reason, now);
        MoveTo(OrderSagaState.Cancelled, now);
        return SagaStep.Then();
    }

    public SagaStep Handle(PaymentSucceeded message, Order order, DateTimeOffset now)
    {
        if (State != OrderSagaState.ProcessingPayment)
        {
            return SagaStep.Ignored;
        }

        PaymentId = message.PaymentId;
        order.Confirm(now);
        MoveTo(OrderSagaState.Completed, now);
        return SagaStep.Then(new OrderConfirmed(order.Id));
    }

    public SagaStep Handle(PaymentFailed message, Order order, DateTimeOffset now)
    {
        if (State != OrderSagaState.ProcessingPayment)
        {
            return SagaStep.Ignored;
        }

        // Compensate the reservation before the order is cancelled.
        FailureReason = message.Reason;
        MoveTo(OrderSagaState.ReleasingStock, now);
        return SagaStep.Then(new ReleaseStock(order.Id, message.Reason));
    }

    public SagaStep Handle(StockReleased message, Order order, DateTimeOffset now)
    {
        if (State != OrderSagaState.ReleasingStock)
        {
            return SagaStep.Ignored;
        }

        order.Cancel(FailureReason ?? "Payment failed", now);
        MoveTo(OrderSagaState.Cancelled, now);
        return SagaStep.Then();
    }

    private void MoveTo(OrderSagaState state, DateTimeOffset now)
    {
        State = state;
        UpdatedAt = now;
    }
}
