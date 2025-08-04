using System.Text.Json;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Outbox;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Payment.Api.Data;
using Payment.Api.Domain;
using Payment.Api.Messaging;

namespace Payment.Tests;

public sealed class PaymentHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly PaymentSimulator _simulator = new(Options.Create(new PaymentSimulationOptions { DeclineAmountsAbove = 1000m }));
    private readonly Guid _orderId = Guid.NewGuid();

    public PaymentHandlerTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task A_succeeded_payment_is_refunded()
    {
        await ChargeAsync(100m);

        await RefundAsync("Shipment could not be created");

        var payment = await SinglePaymentAsync();
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal("Shipment could not be created", payment.RefundReason);
        Assert.Equal(_clock.GetUtcNow(), payment.RefundedAt);
        Assert.Equal(new PaymentRefunded(_orderId, payment.Id), LastMessage<PaymentRefunded>());
    }

    [Fact]
    public async Task Refunding_twice_confirms_both_times_but_refunds_once()
    {
        await ChargeAsync(100m);

        await RefundAsync("first");
        await RefundAsync("second");

        var payment = await SinglePaymentAsync();
        Assert.Equal("first", payment.RefundReason);
        Assert.Equal(2, Messages().Count(m => m.Type == nameof(PaymentRefunded)));
    }

    [Fact]
    public async Task A_declined_payment_has_nothing_to_refund_but_is_confirmed()
    {
        await ChargeAsync(5000m);

        await RefundAsync("Order timed out");

        Assert.Equal(PaymentStatus.Declined, (await SinglePaymentAsync()).Status);
        Assert.Equal(new PaymentRefunded(_orderId, null), LastMessage<PaymentRefunded>());
    }

    [Fact]
    public async Task A_refund_before_the_charge_voids_the_order_and_blocks_a_late_charge()
    {
        await RefundAsync("Timed out waiting for payment");
        await ChargeAsync(100m);

        var payment = await SinglePaymentAsync();
        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Equal(new PaymentRefunded(_orderId, null), LastMessage<PaymentRefunded>());
        Assert.Equal(_orderId, LastMessage<PaymentFailed>().OrderId);
        Assert.DoesNotContain(Messages(), m => m.Type == nameof(PaymentSucceeded));
    }

    [Fact]
    public async Task A_repeated_charge_after_a_refund_is_answered_with_a_failure()
    {
        await ChargeAsync(100m);
        await RefundAsync("Cancelled");

        await ChargeAsync(100m);

        Assert.Equal("The payment was refunded.", LastMessage<PaymentFailed>().Reason);
    }

    public void Dispose() => _connection.Dispose();

    private async Task ChargeAsync(decimal amount)
    {
        await using var db = CreateDb();
        var handler = new ProcessPaymentHandler(db, _simulator, Outbox(db), _clock, NullLogger<ProcessPaymentHandler>.Instance);
        await handler.HandleAsync(new ProcessPayment(_orderId, "customer-1", amount, "USD"), Context<ProcessPayment>(), CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private async Task RefundAsync(string reason)
    {
        await using var db = CreateDb();
        var handler = new RefundPaymentHandler(db, Outbox(db), _clock, NullLogger<RefundPaymentHandler>.Instance);
        await handler.HandleAsync(new RefundPayment(_orderId, "customer-1", 100m, "USD", reason), Context<RefundPayment>(), CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private async Task<PaymentRecord> SinglePaymentAsync()
    {
        await using var db = CreateDb();
        return await db.Payments.SingleAsync();
    }

    private List<OutboxMessage> Messages()
    {
        using var db = CreateDb();
        return db.Set<OutboxMessage>().AsEnumerable().OrderBy(m => m.OccurredOnUtc).ThenBy(m => m.Attempts).ToList();
    }

    private TMessage LastMessage<TMessage>() where TMessage : IIntegrationMessage =>
        JsonSerializer.Deserialize<TMessage>(
            Messages().Last(m => m.Type == typeof(TMessage).Name).Payload, MessagingJson.Options)!;

    private EntityFrameworkOutbox<PaymentDbContext> Outbox(PaymentDbContext db) => new(db, new OutboxSignal(), _clock);

    private PaymentDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<PaymentDbContext>().UseSqlite(_connection).Options);

    private static MessageContext Context<TMessage>() where TMessage : IIntegrationMessage =>
        new(Guid.NewGuid(), MessageType.NameOf<TMessage>(), null);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
