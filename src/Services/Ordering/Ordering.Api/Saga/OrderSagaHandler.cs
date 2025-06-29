using System.Diagnostics.Metrics;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Observability;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Ordering.Api.Data;
using Ordering.Api.Domain;

namespace Ordering.Api.Saga;

/// <summary>
/// Feeds incoming messages to the persisted <see cref="OrderSaga"/> and enqueues the
/// commands it decides on. The inbox commits the saga, the order and the outgoing messages
/// together; a concurrent update of the same saga fails on its row version and is retried.
/// </summary>
public sealed class OrderSagaHandler(
    OrderingDbContext db,
    IOutbox outbox,
    TimeProvider clock,
    ILogger<OrderSagaHandler> logger) :
    IMessageHandler<OrderSubmitted>,
    IMessageHandler<StockReserved>,
    IMessageHandler<StockReservationFailed>,
    IMessageHandler<PaymentSucceeded>,
    IMessageHandler<PaymentFailed>,
    IMessageHandler<StockReleased>
{
    private static readonly Counter<long> Transitions =
        Telemetry.Meter.CreateCounter<long>("ordering.saga.transitions", description: "Order saga state changes");

    public async Task HandleAsync(OrderSubmitted message, MessageContext context, CancellationToken cancellationToken)
    {
        if (await db.OrderSagas.AnyAsync(s => s.OrderId == message.OrderId, cancellationToken))
        {
            logger.LogInformation("Saga for order {OrderId} already started", message.OrderId);
            return;
        }

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == message.OrderId, cancellationToken);
        if (order is null)
        {
            logger.LogWarning("Order {OrderId} not found; cannot start its saga", message.OrderId);
            return;
        }

        var (saga, step) = OrderSaga.Start(order, clock.GetUtcNow());
        db.OrderSagas.Add(saga);
        Send(step);
        Record(order.Id, "Started", saga.State, nameof(OrderSubmitted));
    }

    public Task HandleAsync(StockReserved message, MessageContext context, CancellationToken cancellationToken) =>
        AdvanceAsync(message.OrderId, context, (saga, order, now) => saga.Handle(message, order, now), cancellationToken);

    public Task HandleAsync(StockReservationFailed message, MessageContext context, CancellationToken cancellationToken) =>
        AdvanceAsync(message.OrderId, context, (saga, order, now) => saga.Handle(message, order, now), cancellationToken);

    public Task HandleAsync(PaymentSucceeded message, MessageContext context, CancellationToken cancellationToken) =>
        AdvanceAsync(message.OrderId, context, (saga, order, now) => saga.Handle(message, order, now), cancellationToken);

    public Task HandleAsync(PaymentFailed message, MessageContext context, CancellationToken cancellationToken) =>
        AdvanceAsync(message.OrderId, context, (saga, order, now) => saga.Handle(message, order, now), cancellationToken);

    public Task HandleAsync(StockReleased message, MessageContext context, CancellationToken cancellationToken) =>
        AdvanceAsync(message.OrderId, context, (saga, order, now) => saga.Handle(message, order, now), cancellationToken);

    private async Task AdvanceAsync(
        Guid orderId,
        MessageContext context,
        Func<OrderSaga, Order, DateTimeOffset, SagaStep> transition,
        CancellationToken cancellationToken)
    {
        var saga = await db.OrderSagas.FirstOrDefaultAsync(s => s.OrderId == orderId, cancellationToken);
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (saga is null || order is null)
        {
            logger.LogWarning("No saga for order {OrderId}; ignoring {MessageType}", orderId, context.MessageType);
            return;
        }

        var previous = saga.State;
        var step = transition(saga, order, clock.GetUtcNow());
        if (!step.Applied)
        {
            logger.LogWarning("Saga for order {OrderId} is {State}; ignoring {MessageType}", orderId, saga.State, context.MessageType);
            return;
        }

        Send(step);
        Record(orderId, previous.ToString(), saga.State, context.MessageType);
    }

    private void Send(SagaStep step)
    {
        foreach (var message in step.Messages)
        {
            outbox.Enqueue(message);
        }
    }

    private void Record(Guid orderId, string from, OrderSagaState to, string trigger)
    {
        Transitions.Add(1, new KeyValuePair<string, object?>("to", to.ToString()));
        logger.LogInformation("Saga for order {OrderId}: {From} -> {To} on {MessageType}", orderId, from, to, trigger);
    }
}
