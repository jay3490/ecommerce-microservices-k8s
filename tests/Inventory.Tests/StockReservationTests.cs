using Inventory.Api.Domain;

namespace Inventory.Tests;

public sealed class StockReservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Keyboard = Guid.NewGuid();
    private static readonly Guid Mouse = Guid.NewGuid();

    private readonly Dictionary<Guid, StockItem> _stock = new()
    {
        [Keyboard] = StockItem.Create(Keyboard, 5, Now),
        [Mouse] = StockItem.Create(Mouse, 1, Now),
    };

    [Fact]
    public void Reserving_moves_quantities_from_available_to_reserved()
    {
        var result = Reserve(new ReservationLine(Keyboard, 2), new ReservationLine(Mouse, 1));

        Assert.True(result.Succeeded);
        Assert.Equal(ReservationStatus.Reserved, result.Reservation!.Status);
        Assert.Equal((3, 2), (_stock[Keyboard].Available, _stock[Keyboard].Reserved));
        Assert.Equal((0, 1), (_stock[Mouse].Available, _stock[Mouse].Reserved));
    }

    [Fact]
    public void Repeated_products_are_reserved_as_one_line()
    {
        var result = Reserve(new ReservationLine(Keyboard, 2), new ReservationLine(Keyboard, 3));

        var line = Assert.Single(result.Reservation!.Lines);
        Assert.Equal(5, line.Quantity);
        Assert.Equal(0, _stock[Keyboard].Available);
    }

    [Fact]
    public void Reservation_is_all_or_nothing()
    {
        var result = Reserve(new ReservationLine(Keyboard, 2), new ReservationLine(Mouse, 2));

        Assert.False(result.Succeeded);
        Assert.Contains("Insufficient stock", result.FailureReason);
        Assert.Equal((5, 0), (_stock[Keyboard].Available, _stock[Keyboard].Reserved));
        Assert.Equal((1, 0), (_stock[Mouse].Available, _stock[Mouse].Reserved));
    }

    [Fact]
    public void Unknown_products_cannot_be_reserved()
    {
        var result = Reserve(new ReservationLine(Guid.NewGuid(), 1));

        Assert.False(result.Succeeded);
        Assert.Contains("not stocked", result.FailureReason);
    }

    [Fact]
    public void Releasing_returns_the_stock_once()
    {
        var reservation = Reserve(new ReservationLine(Keyboard, 2)).Reservation!;

        Assert.True(reservation.Release(_stock, Now));
        Assert.False(reservation.Release(_stock, Now));

        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.Equal((5, 0), (_stock[Keyboard].Available, _stock[Keyboard].Reserved));
    }

    [Fact]
    public void Committing_removes_the_reserved_stock_and_blocks_a_later_release()
    {
        var reservation = Reserve(new ReservationLine(Keyboard, 2)).Reservation!;

        Assert.True(reservation.Commit(_stock, Now));
        Assert.False(reservation.Release(_stock, Now));

        Assert.Equal(ReservationStatus.Committed, reservation.Status);
        Assert.Equal((3, 0), (_stock[Keyboard].Available, _stock[Keyboard].Reserved));
    }

    private ReservationResult Reserve(params ReservationLine[] lines) =>
        StockReservation.TryReserve(Guid.NewGuid(), lines, _stock, Now);
}
