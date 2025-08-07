using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Shipping.Api.Data;
using Shipping.Api.Domain;

namespace Shipping.Api.Messaging;

/// <summary>
/// Shipping's side of the order saga. One shipment row per order makes both commands
/// idempotent: a repeated CreateShipment gets the stored outcome, and a CancelShipment that
/// overtakes its CreateShipment leaves a cancelled row that the late command is refused by.
/// </summary>
public sealed class ShipmentHandlers(
    ShippingDbContext db,
    ShippingPolicy policy,
    IOutbox outbox,
    TimeProvider clock,
    ILogger<ShipmentHandlers> logger) :
    IMessageHandler<CreateShipment>,
    IMessageHandler<CancelShipment>
{
    public async Task HandleAsync(CreateShipment message, MessageContext context, CancellationToken cancellationToken)
    {
        var shipment = await FindAsync(message.OrderId, cancellationToken);
        if (shipment is null)
        {
            var lines = message.Lines.Select(line => new ShipmentLine(line.ProductId, line.Quantity)).ToList();
            var decision = policy.Evaluate(message.CustomerId, lines);
            var now = clock.GetUtcNow();
            shipment = decision.Accepted
                ? Shipment.Book(message.OrderId, message.CustomerId, lines, policy.Carrier, ShippingPolicy.NewTrackingNumber(), now)
                : Shipment.Reject(message.OrderId, message.CustomerId, lines, decision.Reason!, now);
            db.Shipments.Add(shipment);

            logger.LogInformation("Shipment {ShipmentId} for order {OrderId}: {Status} {Reason}",
                shipment.Id, message.OrderId, shipment.Status, shipment.FailureReason);
        }
        else
        {
            logger.LogInformation("Order {OrderId} already has shipment {ShipmentId} ({Status}); repeating the outcome",
                message.OrderId, shipment.Id, shipment.Status);
        }

        if (shipment.IsBooked)
        {
            outbox.Enqueue(new ShipmentCreated(message.OrderId, shipment.Id, shipment.TrackingNumber!));
        }
        else
        {
            outbox.Enqueue(new ShipmentCreationFailed(message.OrderId, shipment.FailureReason ?? "The shipment was cancelled."));
        }
    }

    public async Task HandleAsync(CancelShipment message, MessageContext context, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var shipment = await FindAsync(message.OrderId, cancellationToken);
        if (shipment is null)
        {
            db.Shipments.Add(Shipment.CancelledBeforeBooking(message.OrderId, message.Reason, now));
            logger.LogInformation("Recorded order {OrderId} as cancelled before a shipment was booked", message.OrderId);
            return;
        }

        if (shipment.Cancel(message.Reason, now))
        {
            logger.LogInformation("Cancelled shipment {ShipmentId} of order {OrderId}: {Reason}", shipment.Id, message.OrderId, message.Reason);
        }
        else if (shipment.Status is ShipmentStatus.Dispatched or ShipmentStatus.Delivered)
        {
            // The parcel is gone; recovering it is a manual (returns) process.
            logger.LogError("Order {OrderId} was cancelled but shipment {ShipmentId} is already {Status}",
                message.OrderId, shipment.Id, shipment.Status);
        }
    }

    private Task<Shipment?> FindAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.Shipments.FirstOrDefaultAsync(s => s.OrderId == orderId, cancellationToken);
}
