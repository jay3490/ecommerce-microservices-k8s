using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Ordering.Api.Data;
using Ordering.Api.Domain;

namespace Ordering.Api.Orders;

/// <summary>
/// Places the order for a basket checkout. The basket service chose the order id, so a
/// checkout that arrives twice (a retried checkout, or a redelivery that slipped past the
/// inbox) finds the order already there and changes nothing.
/// </summary>
public sealed class BasketCheckoutHandler(
    OrderingDbContext db,
    IOutbox outbox,
    TimeProvider clock,
    ILogger<BasketCheckoutHandler> logger) : IMessageHandler<BasketCheckedOut>
{
    public async Task HandleAsync(BasketCheckedOut message, MessageContext context, CancellationToken cancellationToken)
    {
        if (await db.Orders.AnyAsync(o => o.Id == message.OrderId, cancellationToken))
        {
            logger.LogInformation("Order {OrderId} already exists; ignoring the repeated checkout", message.OrderId);
            return;
        }

        if (message.Lines.Count == 0)
        {
            logger.LogError("Checkout {OrderId} has no lines; no order was placed", message.OrderId);
            return;
        }

        Order order;
        try
        {
            var items = message.Lines.Select(line =>
                new OrderItem(line.ProductId, line.ProductName, line.UnitPrice, line.Currency, line.Quantity));
            order = Order.Place(message.OrderId, message.CustomerId, message.Lines[0].Currency, items, clock.GetUtcNow());
        }
        catch (OrderingDomainException ex)
        {
            // Retrying cannot fix a checkout that breaks an order invariant, so it is consumed.
            logger.LogError(ex, "Checkout {OrderId} was rejected: {Reason}", message.OrderId, ex.Message);
            return;
        }

        db.Orders.Add(order);
        outbox.Enqueue(OrderMessages.Submitted(order));
        logger.LogInformation("Order {OrderId} placed from a basket checkout for {Total} {Currency}",
            order.Id, order.Total, order.Currency);
    }
}

public static class OrderMessages
{
    public static OrderSubmitted Submitted(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Total,
        order.Currency,
        order.Lines.Select(line => new OrderLineItem(line.ProductId, line.Quantity)).ToList());
}
