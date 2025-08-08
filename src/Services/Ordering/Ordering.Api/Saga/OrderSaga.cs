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

    /// <summary>Paid, CreateShipment sent, waiting for the shipping service.</summary>
    CreatingShipment,

    /// <summary>A step after payment failed; compensating by refunding the payment.</summary>
    RefundingPayment,

    /// <summary>Compensating by asking inventory to release the reserved stock.</summary>
    ReleasingStock,

    /// <summary>Paid, booked for shipping and confirmed.</summary>
    Completed,

    /// <summary>Stopped by a failure, with every completed step undone.</summary>
    Cancelled,
}

/// <summary>The outcome of feeding one message to the saga.</summary>
public sealed record SagaStep(bool Applied, IReadOnlyList<IIntegrationMessage> Messages)
{
    public static readonly SagaStep Ignored = new(false, []);

    public static SagaStep Then(params IIntegrationMessage[] messages) => new(true, messages);
}

/// <summary>
/// Orchestrates one order: reserve stock, take payment, book the shipment, confirm. A failure
/// undoes the completed steps in reverse order (refund, then release the stock) before the
/// order is cancelled. The state is persisted after every step, so the saga resumes correctly
/// after a restart. Each method accepts a message only in the state that expects it;
/// anything else (a late duplicate, a reply for an older step) is ignored and leaves the
/// saga unchanged.
/// </summary>
//
//  ReservingStock --StockReserved--> ProcessingPayment --PaymentSucceeded--> CreatingShipment --ShipmentCreated--> Completed
//        |                                  |                                       |
//   StockReservationFailed            PaymentFailed                      ShipmentCreationFailed
//        |                                  |                                       v
//        |                                  |                               RefundingPayment
//        |                                  v                                       |
//        |                            ReleasingStock <-------PaymentRefunded--------+
//        v                                  |
//    Cancelled <-------StockReleased--------+
//
//  A timeout in ReservingStock goes to ReleasingStock; in ProcessingPayment or CreatingShipment
//  it goes to RefundingPayment (also cancelling the shipment); see TimeOut.
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
    public Guid? ShipmentId { get; private set; }
    public string? TrackingNumber { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>How many times a step of this saga has timed out.</summary>
    public int Timeouts { get; private set; }

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

        return (saga, SagaStep.Then(new ReserveStock(order.Id, Lines(order))));
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
        return Cancel(order, now);
    }

    public SagaStep Handle(PaymentSucceeded message, Order order, DateTimeOffset now)
    {
        if (State != OrderSagaState.ProcessingPayment)
        {
            return SagaStep.Ignored;
        }

        PaymentId = message.PaymentId;
        MoveTo(OrderSagaState.CreatingShipment, now);
        return SagaStep.Then(new CreateShipment(order.Id, order.CustomerId, Lines(order)));
    }

    public SagaStep Handle(PaymentFailed message, Order order, DateTimeOffset now)
    {
        if (State != OrderSagaState.ProcessingPayment)
        {
            return SagaStep.Ignored;
        }

        // No money was taken; only the reservation has to be undone.
        FailureReason = message.Reason;
        MoveTo(OrderSagaState.ReleasingStock, now);
        return SagaStep.Then(new ReleaseStock(order.Id, message.Reason));
    }

    public SagaStep Handle(ShipmentCreated message, Order order, DateTimeOffset now)
    {
        if (State != OrderSagaState.CreatingShipment)
        {
            return SagaStep.Ignored;
        }

        ShipmentId = message.ShipmentId;
        TrackingNumber = message.TrackingNumber;
        order.Confirm(now);
        MoveTo(OrderSagaState.Completed, now);
        return SagaStep.Then(new OrderConfirmed(order.Id, order.CustomerId, order.Total, order.Currency));
    }

    public SagaStep Handle(ShipmentCreationFailed message, Order order, DateTimeOffset now)
    {
        if (State != OrderSagaState.CreatingShipment)
        {
            return SagaStep.Ignored;
        }

        // Undo in reverse order: the payment first, then the reservation.
        FailureReason = message.Reason;
        MoveTo(OrderSagaState.RefundingPayment, now);
        return SagaStep.Then(Refund(order));
    }

    public SagaStep Handle(PaymentRefunded message, Order order, DateTimeOffset now)
    {
        if (State != OrderSagaState.RefundingPayment)
        {
            return SagaStep.Ignored;
        }

        MoveTo(OrderSagaState.ReleasingStock, now);
        return SagaStep.Then(new ReleaseStock(order.Id, FailureReason ?? "Order cancelled"));
    }

    public SagaStep Handle(StockReleased message, Order order, DateTimeOffset now)
    {
        if (State != OrderSagaState.ReleasingStock)
        {
            return SagaStep.Ignored;
        }

        return Cancel(order, now);
    }

    /// <summary>
    /// Called when the saga has waited longer than the step timeout. A forward step is given
    /// up: everything that might have happened is compensated, because the late reply could
    /// still be on its way (refunding a payment that was never taken voids it; releasing stock
    /// that was never reserved blocks a late reservation). A compensation step that has not
    /// been confirmed is simply asked for again.
    /// </summary>
    public SagaStep TimeOut(Order order, DateTimeOffset now)
    {
        switch (State)
        {
            case OrderSagaState.ReservingStock:
                FailureReason = "Timed out waiting for the inventory service.";
                Timeouts++;
                MoveTo(OrderSagaState.ReleasingStock, now);
                return SagaStep.Then(new ReleaseStock(order.Id, FailureReason));

            case OrderSagaState.ProcessingPayment:
                FailureReason = "Timed out waiting for the payment service.";
                Timeouts++;
                MoveTo(OrderSagaState.RefundingPayment, now);
                return SagaStep.Then(Refund(order));

            case OrderSagaState.CreatingShipment:
                FailureReason = "Timed out waiting for the shipping service.";
                Timeouts++;
                MoveTo(OrderSagaState.RefundingPayment, now);
                return SagaStep.Then(new CancelShipment(order.Id, FailureReason), Refund(order));

            case OrderSagaState.RefundingPayment:
                Timeouts++;
                MoveTo(State, now);
                return SagaStep.Then(Refund(order));

            case OrderSagaState.ReleasingStock:
                Timeouts++;
                MoveTo(State, now);
                return SagaStep.Then(new ReleaseStock(order.Id, FailureReason ?? "Order cancelled"));

            default:
                return SagaStep.Ignored;
        }
    }

    private RefundPayment Refund(Order order) =>
        new(order.Id, order.CustomerId, order.Total, order.Currency, FailureReason ?? "Order cancelled");

    private SagaStep Cancel(Order order, DateTimeOffset now)
    {
        var reason = FailureReason ?? "Order cancelled";
        order.Cancel(reason, now);
        MoveTo(OrderSagaState.Cancelled, now);
        return SagaStep.Then(new OrderCancelled(order.Id, order.CustomerId, reason));
    }

    private void MoveTo(OrderSagaState state, DateTimeOffset now)
    {
        State = state;
        UpdatedAt = now;
    }

    private static List<OrderLineItem> Lines(Order order) =>
        order.Lines.Select(line => new OrderLineItem(line.ProductId, line.Quantity)).ToList();
}
