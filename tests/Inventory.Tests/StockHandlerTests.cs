using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Outbox;
using Inventory.Api.Data;
using Inventory.Api.Domain;
using Inventory.Api.Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inventory.Tests;

public sealed class StockHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Keyboard = Guid.NewGuid();

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Guid _orderId = Guid.NewGuid();

    public StockHandlerTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
        db.StockItems.Add(StockItem.Create(Keyboard, 5, Now));
        db.SaveChanges();
    }

    [Fact]
    public async Task Releasing_before_reserving_confirms_and_blocks_a_late_reservation()
    {
        await HandleAsync(h => h.HandleAsync(new ReleaseStock(_orderId, "Timed out"), Context<ReleaseStock>(), CancellationToken.None));
        await HandleAsync(h => h.HandleAsync(new ReserveStock(_orderId, [new OrderLineItem(Keyboard, 2)]), Context<ReserveStock>(), CancellationToken.None));

        await using var db = CreateDb();
        var reservation = await db.Reservations.SingleAsync();
        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.Empty(reservation.Lines);
        Assert.Equal(5, (await db.StockItems.SingleAsync(s => s.ProductId == Keyboard)).Available);
        Assert.Equal([nameof(StockReleased)], await db.Set<OutboxMessage>().Select(m => m.Type).ToListAsync());
    }

    [Fact]
    public async Task Releasing_a_held_reservation_returns_the_stock()
    {
        await HandleAsync(h => h.HandleAsync(new ReserveStock(_orderId, [new OrderLineItem(Keyboard, 2)]), Context<ReserveStock>(), CancellationToken.None));
        await HandleAsync(h => h.HandleAsync(new ReleaseStock(_orderId, "Payment failed"), Context<ReleaseStock>(), CancellationToken.None));

        await using var db = CreateDb();
        Assert.Equal(ReservationStatus.Released, (await db.Reservations.SingleAsync()).Status);
        Assert.Equal((5, 0), await db.StockItems.Where(s => s.ProductId == Keyboard).Select(s => ValueTuple.Create(s.Available, s.Reserved)).SingleAsync());
        Assert.Equal([nameof(StockReleased), nameof(StockReserved)], (await db.Set<OutboxMessage>().Select(m => m.Type).ToListAsync()).Order());
    }

    public void Dispose() => _connection.Dispose();

    private async Task HandleAsync(Func<StockHandlers, Task> handle)
    {
        await using var db = CreateDb();
        var clock = new FixedClock(Now);
        var handlers = new StockHandlers(db, new EntityFrameworkOutbox<InventoryDbContext>(db, new OutboxSignal(), clock), clock,
            NullLogger<StockHandlers>.Instance);
        await handle(handlers);
        await db.SaveChangesAsync();
    }

    private InventoryDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(_connection).Options);

    private static MessageContext Context<TMessage>() where TMessage : IIntegrationMessage =>
        new(Guid.NewGuid(), MessageType.NameOf<TMessage>(), null);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
