using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Outbox;
using Inventory.Api.Data;
using Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Messaging;

/// <summary>
/// Inventory's side of the order saga. Replies go through the outbox in the same transaction
/// as the stock changes. Commands are also idempotent at the business level: a repeated
/// ReserveStock for an order that already holds stock answers with the existing reservation.
/// </summary>
public sealed class StockHandlers(
    InventoryDbContext db,
    IOutbox outbox,
    TimeProvider clock,
    ILogger<StockHandlers> logger) :
    IMessageHandler<ReserveStock>,
    IMessageHandler<ReleaseStock>,
    IMessageHandler<OrderConfirmed>
{
    public async Task HandleAsync(ReserveStock message, MessageContext context, CancellationToken cancellationToken)
    {
        var existing = await FindReservationAsync(message.OrderId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status != ReservationStatus.Released)
            {
                outbox.Enqueue(new StockReserved(message.OrderId, existing.Id));
            }

            logger.LogInformation("Order {OrderId} already has reservation {ReservationId} ({Status})",
                message.OrderId, existing.Id, existing.Status);
            return;
        }

        var lines = message.Lines.Select(line => new ReservationLine(line.ProductId, line.Quantity)).ToList();
        var stock = await db.LoadStockAsync(lines.Select(line => line.ProductId), cancellationToken);
        var result = StockReservation.TryReserve(message.OrderId, lines, stock, clock.GetUtcNow());

        if (!result.Succeeded)
        {
            logger.LogInformation("Could not reserve stock for order {OrderId}: {Reason}", message.OrderId, result.FailureReason);
            outbox.Enqueue(new StockReservationFailed(message.OrderId, result.FailureReason!));
            return;
        }

        db.Reservations.Add(result.Reservation!);
        outbox.Enqueue(new StockReserved(message.OrderId, result.Reservation!.Id));
        logger.LogInformation("Reserved stock for order {OrderId} as {ReservationId}", message.OrderId, result.Reservation.Id);
    }

    public async Task HandleAsync(ReleaseStock message, MessageContext context, CancellationToken cancellationToken)
    {
        var reservation = await FindReservationAsync(message.OrderId, cancellationToken);
        if (reservation is null)
        {
            // Nothing was held (or it is already gone); confirm so the saga can finish.
            outbox.Enqueue(new StockReleased(message.OrderId));
            return;
        }

        if (reservation.Status == ReservationStatus.Committed)
        {
            logger.LogError("Order {OrderId} asked to release committed reservation {ReservationId}; ignoring",
                message.OrderId, reservation.Id);
            return;
        }

        var stock = await db.LoadStockAsync(reservation.Lines.Select(line => line.ProductId), cancellationToken);
        if (reservation.Release(stock, clock.GetUtcNow()))
        {
            logger.LogInformation("Released reservation {ReservationId} for order {OrderId}: {Reason}",
                reservation.Id, message.OrderId, message.Reason);
        }

        outbox.Enqueue(new StockReleased(message.OrderId));
    }

    public async Task HandleAsync(OrderConfirmed message, MessageContext context, CancellationToken cancellationToken)
    {
        var reservation = await FindReservationAsync(message.OrderId, cancellationToken);
        if (reservation is null)
        {
            logger.LogWarning("Order {OrderId} was confirmed but has no reservation", message.OrderId);
            return;
        }

        var stock = await db.LoadStockAsync(reservation.Lines.Select(line => line.ProductId), cancellationToken);
        if (reservation.Commit(stock, clock.GetUtcNow()))
        {
            logger.LogInformation("Committed reservation {ReservationId} for order {OrderId}", reservation.Id, message.OrderId);
        }
    }

    private Task<StockReservation?> FindReservationAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.Reservations.FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);
}
