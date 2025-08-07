using System.Text.Json;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Outbox;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shipping.Api.Data;
using Shipping.Api.Domain;
using Shipping.Api.Messaging;

namespace Shipping.Tests;

public sealed class ShipmentHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly ShippingPolicy _policy = new(Options.Create(new ShippingRulesOptions
    {
        Carrier = "Test Carrier",
        MaxUnitsPerShipment = 10,
        UnserviceableCustomerIds = ["far-away"],
    }));

    private readonly Guid _orderId = Guid.NewGuid();

    public ShipmentHandlerTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task A_shippable_order_gets_a_pending_shipment_with_a_tracking_number()
    {
        await CreateAsync("customer-1", 3);

        var shipment = await SingleShipmentAsync();
        Assert.Equal(ShipmentStatus.Pending, shipment.Status);
        Assert.Equal("Test Carrier", shipment.Carrier);
        Assert.Matches("^SP[0-9]{10}$", shipment.TrackingNumber);
        Assert.Equal(new ShipmentCreated(_orderId, shipment.Id, shipment.TrackingNumber!), Last<ShipmentCreated>());
    }

    [Theory]
    [InlineData("far-away", 1, "does not deliver")]
    [InlineData("customer-1", 11, "parcel limit")]
    public async Task Orders_the_carrier_cannot_take_are_rejected(string customerId, int quantity, string reason)
    {
        await CreateAsync(customerId, quantity);

        Assert.Equal(ShipmentStatus.Rejected, (await SingleShipmentAsync()).Status);
        Assert.Contains(reason, Last<ShipmentCreationFailed>().Reason);
    }

    [Fact]
    public async Task A_repeated_command_repeats_the_outcome_without_a_second_shipment()
    {
        await CreateAsync("customer-1", 1);
        await CreateAsync("customer-1", 1);

        var shipment = await SingleShipmentAsync();
        Assert.Equal(2, Messages().Count(m => m.Type == nameof(ShipmentCreated)));
        Assert.All(Messages(), m => Assert.Contains(shipment.TrackingNumber!, m.Payload));
    }

    [Fact]
    public async Task Cancelling_a_pending_shipment_cancels_it()
    {
        await CreateAsync("customer-1", 1);

        await CancelAsync("Payment refunded");

        var shipment = await SingleShipmentAsync();
        Assert.Equal(ShipmentStatus.Cancelled, shipment.Status);
        Assert.Equal("Payment refunded", shipment.FailureReason);
    }

    [Fact]
    public async Task A_cancellation_that_overtakes_the_booking_makes_the_late_booking_fail()
    {
        await CancelAsync("Order timed out");
        await CreateAsync("customer-1", 1);

        Assert.Equal(ShipmentStatus.Cancelled, (await SingleShipmentAsync()).Status);
        Assert.Equal(_orderId, Last<ShipmentCreationFailed>().OrderId);
        Assert.DoesNotContain(Messages(), m => m.Type == nameof(ShipmentCreated));
    }

    [Fact]
    public async Task A_dispatched_parcel_is_not_cancelled()
    {
        await CreateAsync("customer-1", 1);
        await using (var db = CreateDb())
        {
            (await db.Shipments.SingleAsync()).Dispatch(_clock.GetUtcNow());
            await db.SaveChangesAsync();
        }

        await CancelAsync("too late");

        Assert.Equal(ShipmentStatus.Dispatched, (await SingleShipmentAsync()).Status);
    }

    public void Dispose() => _connection.Dispose();

    private async Task CreateAsync(string customerId, int quantity)
    {
        await using var db = CreateDb();
        var command = new CreateShipment(_orderId, customerId, [new OrderLineItem(Guid.NewGuid(), quantity)]);
        await Handler(db).HandleAsync(command, Context<CreateShipment>(), CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private async Task CancelAsync(string reason)
    {
        await using var db = CreateDb();
        await Handler(db).HandleAsync(new CancelShipment(_orderId, reason), Context<CancelShipment>(), CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private ShipmentHandlers Handler(ShippingDbContext db) =>
        new(db, _policy, new EntityFrameworkOutbox<ShippingDbContext>(db, new OutboxSignal(), _clock), _clock,
            NullLogger<ShipmentHandlers>.Instance);

    private async Task<Shipment> SingleShipmentAsync()
    {
        await using var db = CreateDb();
        return await db.Shipments.SingleAsync();
    }

    private List<OutboxMessage> Messages()
    {
        using var db = CreateDb();
        return db.Set<OutboxMessage>().ToList();
    }

    private TMessage Last<TMessage>() where TMessage : IIntegrationMessage =>
        JsonSerializer.Deserialize<TMessage>(Messages().Last(m => m.Type == typeof(TMessage).Name).Payload, MessagingJson.Options)!;

    private ShippingDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ShippingDbContext>().UseSqlite(_connection).Options);

    private static MessageContext Context<TMessage>() where TMessage : IIntegrationMessage =>
        new(Guid.NewGuid(), MessageType.NameOf<TMessage>(), null);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
