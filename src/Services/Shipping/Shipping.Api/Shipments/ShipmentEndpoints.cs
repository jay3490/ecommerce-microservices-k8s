using BuildingBlocks.Http;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Outbox;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shipping.Api.Data;
using Shipping.Api.Domain;

namespace Shipping.Api.Shipments;

public sealed record TrackingEventDto(string Status, DateTimeOffset At, string? Note);

public sealed record ShipmentDto(
    Guid Id,
    Guid OrderId,
    string Status,
    string? Carrier,
    string? TrackingNumber,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TrackingEventDto> History)
{
    public static ShipmentDto From(Shipment shipment) => new(
        shipment.Id,
        shipment.OrderId,
        shipment.Status.ToString(),
        shipment.Carrier,
        shipment.TrackingNumber,
        shipment.FailureReason,
        shipment.CreatedAt,
        shipment.UpdatedAt,
        shipment.Events.OrderBy(e => e.At).Select(e => new TrackingEventDto(e.Status.ToString(), e.At, e.Note)).ToList());
}

/// <summary>
/// Tracking for customers (their own orders only) and the two status updates an operator or
/// a carrier integration would post. The gateway restricts the POST routes to admins.
/// </summary>
public static class ShipmentEndpoints
{
    public static IEndpointRouteBuilder MapShipmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var shipments = endpoints.MapGroup("/api/shipments").WithTags("Shipments");

        shipments.MapGet("/{orderId:guid}", GetShipment);
        shipments.MapPost("/{orderId:guid}/dispatch", Dispatch);
        shipments.MapPost("/{orderId:guid}/deliver", Deliver);

        return endpoints;
    }

    private static async Task<IResult> GetShipment(
        Guid orderId,
        [FromHeader(Name = KnownHeaders.CustomerId)] string? customerId,
        ShippingDbContext db,
        CancellationToken cancellationToken)
    {
        var shipment = await db.Shipments.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrderId == orderId && s.CustomerId == customerId, cancellationToken);
        return shipment is null ? Results.NotFound() : Results.Ok(ShipmentDto.From(shipment));
    }

    private static Task<IResult> Dispatch(
        Guid orderId, ShippingDbContext db, IOutbox outbox, TimeProvider clock, CancellationToken cancellationToken) =>
        ChangeAsync(orderId, db, cancellationToken, shipment =>
        {
            shipment.Dispatch(clock.GetUtcNow());
            outbox.Enqueue(new ShipmentDispatched(
                shipment.OrderId, shipment.Id, shipment.CustomerId, shipment.Carrier!, shipment.TrackingNumber!));
        });

    private static Task<IResult> Deliver(
        Guid orderId, ShippingDbContext db, IOutbox outbox, TimeProvider clock, CancellationToken cancellationToken) =>
        ChangeAsync(orderId, db, cancellationToken, shipment =>
        {
            shipment.Deliver(clock.GetUtcNow());
            outbox.Enqueue(new ShipmentDelivered(shipment.OrderId, shipment.Id, shipment.CustomerId));
        });

    private static async Task<IResult> ChangeAsync(
        Guid orderId, ShippingDbContext db, CancellationToken cancellationToken, Action<Shipment> change)
    {
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.OrderId == orderId, cancellationToken);
        if (shipment is null)
        {
            return Results.NotFound();
        }

        try
        {
            change(shipment);
        }
        catch (ShippingDomainException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The shipment changed meanwhile; reload and retry.");
        }

        return Results.Ok(ShipmentDto.From(shipment));
    }
}
