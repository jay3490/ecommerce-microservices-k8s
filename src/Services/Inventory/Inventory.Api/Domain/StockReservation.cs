namespace Inventory.Api.Domain;

public enum ReservationStatus
{
    /// <summary>Stock is held for the order.</summary>
    Reserved,

    /// <summary>The order failed and the stock went back to Available.</summary>
    Released,

    /// <summary>The order was confirmed and the stock has left the warehouse's books.</summary>
    Committed,
}

public sealed record ReservationLine(Guid ProductId, int Quantity);

public sealed record ReservationResult(StockReservation? Reservation, string? FailureReason)
{
    public bool Succeeded => Reservation is not null;
}

/// <summary>
/// Stock held for one order. Reserving is all-or-nothing: either every line is covered and
/// all quantities move to Reserved, or nothing changes.
/// </summary>
public sealed class StockReservation
{
    private readonly List<ReservationLine> _lines = [];

    private StockReservation()
    {
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<ReservationLine> Lines => _lines.AsReadOnly();

    public static ReservationResult TryReserve(
        Guid orderId,
        IEnumerable<ReservationLine> requested,
        IReadOnlyDictionary<Guid, StockItem> stock,
        DateTimeOffset now)
    {
        var lines = requested
            .GroupBy(line => line.ProductId)
            .Select(group => new ReservationLine(group.Key, group.Sum(line => line.Quantity)))
            .ToList();

        if (lines.Count == 0 || lines.Any(line => line.Quantity <= 0))
        {
            return new ReservationResult(null, "The reservation request has no valid lines.");
        }

        foreach (var line in lines)
        {
            if (!stock.TryGetValue(line.ProductId, out var item))
            {
                return new ReservationResult(null, $"Product {line.ProductId} is not stocked.");
            }

            if (item.Available < line.Quantity)
            {
                return new ReservationResult(null,
                    $"Insufficient stock for product {line.ProductId}: requested {line.Quantity}, available {item.Available}.");
            }
        }

        foreach (var line in lines)
        {
            stock[line.ProductId].Reserve(line.Quantity, now);
        }

        var reservation = new StockReservation
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Status = ReservationStatus.Reserved,
            CreatedAt = now,
            UpdatedAt = now,
        };
        reservation._lines.AddRange(lines);
        return new ReservationResult(reservation, null);
    }

    /// <summary>
    /// Records that the order's stock was released before anything was reserved (the saga
    /// timed out waiting for inventory). A ReserveStock for the order that arrives later finds
    /// this released reservation and reserves nothing.
    /// </summary>
    public static StockReservation ReleasedBeforeReserving(Guid orderId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Status = ReservationStatus.Released,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <returns><c>false</c> if the reservation was not in the Reserved state.</returns>
    public bool Release(IReadOnlyDictionary<Guid, StockItem> stock, DateTimeOffset now) =>
        Settle(ReservationStatus.Released, stock, now, (item, quantity) => item.Release(quantity, now));

    /// <returns><c>false</c> if the reservation was not in the Reserved state.</returns>
    public bool Commit(IReadOnlyDictionary<Guid, StockItem> stock, DateTimeOffset now) =>
        Settle(ReservationStatus.Committed, stock, now, (item, quantity) => item.Commit(quantity, now));

    private bool Settle(
        ReservationStatus target,
        IReadOnlyDictionary<Guid, StockItem> stock,
        DateTimeOffset now,
        Action<StockItem, int> apply)
    {
        if (Status != ReservationStatus.Reserved)
        {
            return false;
        }

        foreach (var line in _lines)
        {
            apply(stock[line.ProductId], line.Quantity);
        }

        Status = target;
        UpdatedAt = now;
        return true;
    }
}
