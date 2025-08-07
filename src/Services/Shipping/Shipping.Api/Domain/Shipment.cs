namespace Shipping.Api.Domain;

public enum ShipmentStatus
{
    /// <summary>Booked and waiting to leave the warehouse.</summary>
    Pending,

    Dispatched,
    Delivered,

    /// <summary>The order was cancelled before the parcel left.</summary>
    Cancelled,

    /// <summary>Could not be booked; the saga compensates the order.</summary>
    Rejected,
}

public sealed record ShipmentLine(Guid ProductId, int Quantity);

/// <summary>One status change, kept as the shipment's tracking history.</summary>
public sealed record TrackingEvent(ShipmentStatus Status, DateTimeOffset At, string? Note);

/// <summary>
/// The shipment of one order. It moves forward only:
/// Pending -> Dispatched -> Delivered, or Pending -> Cancelled. Rejected and Cancelled
/// shipments are kept so a repeated or late command gets the same answer.
/// </summary>
public sealed class Shipment
{
    private readonly List<ShipmentLine> _lines = [];
    private readonly List<TrackingEvent> _events = [];

    private Shipment()
    {
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string CustomerId { get; private set; } = string.Empty;
    public ShipmentStatus Status { get; private set; }
    public string? Carrier { get; private set; }
    public string? TrackingNumber { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<ShipmentLine> Lines => _lines.AsReadOnly();
    public IReadOnlyList<TrackingEvent> Events => _events.AsReadOnly();

    /// <summary>Whether the shipment stands for a booked parcel (the saga's "created" answer).</summary>
    public bool IsBooked => Status is ShipmentStatus.Pending or ShipmentStatus.Dispatched or ShipmentStatus.Delivered;

    public static Shipment Book(
        Guid orderId, string customerId, IEnumerable<ShipmentLine> lines, string carrier, string trackingNumber, DateTimeOffset now)
    {
        var shipment = New(orderId, customerId, ShipmentStatus.Pending, now);
        shipment._lines.AddRange(lines);
        shipment.Carrier = carrier;
        shipment.TrackingNumber = trackingNumber;
        shipment._events.Add(new TrackingEvent(ShipmentStatus.Pending, now, $"Booked with {carrier}"));
        return shipment;
    }

    public static Shipment Reject(Guid orderId, string customerId, IEnumerable<ShipmentLine> lines, string reason, DateTimeOffset now)
    {
        var shipment = New(orderId, customerId, ShipmentStatus.Rejected, now);
        shipment._lines.AddRange(lines);
        shipment.FailureReason = reason;
        shipment._events.Add(new TrackingEvent(ShipmentStatus.Rejected, now, reason));
        return shipment;
    }

    /// <summary>A cancellation that arrived before the shipment was ever requested.</summary>
    public static Shipment CancelledBeforeBooking(Guid orderId, string reason, DateTimeOffset now)
    {
        var shipment = New(orderId, string.Empty, ShipmentStatus.Cancelled, now);
        shipment.FailureReason = reason;
        shipment._events.Add(new TrackingEvent(ShipmentStatus.Cancelled, now, reason));
        return shipment;
    }

    /// <returns><c>false</c> if the parcel already left or the shipment is closed.</returns>
    public bool Cancel(string reason, DateTimeOffset now)
    {
        if (Status != ShipmentStatus.Pending)
        {
            return false;
        }

        FailureReason = reason;
        MoveTo(ShipmentStatus.Cancelled, now, reason);
        return true;
    }

    public void Dispatch(DateTimeOffset now)
    {
        if (Status != ShipmentStatus.Pending)
        {
            throw new ShippingDomainException($"A {Status} shipment cannot be dispatched.");
        }

        MoveTo(ShipmentStatus.Dispatched, now, $"Handed to {Carrier}");
    }

    public void Deliver(DateTimeOffset now)
    {
        if (Status != ShipmentStatus.Dispatched)
        {
            throw new ShippingDomainException($"A {Status} shipment cannot be delivered.");
        }

        MoveTo(ShipmentStatus.Delivered, now, null);
    }

    private static Shipment New(Guid orderId, string customerId, ShipmentStatus status, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        CustomerId = customerId,
        Status = status,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private void MoveTo(ShipmentStatus status, DateTimeOffset now, string? note)
    {
        Status = status;
        UpdatedAt = now;
        _events.Add(new TrackingEvent(status, now, note));
    }
}

public sealed class ShippingDomainException(string message) : Exception(message);
