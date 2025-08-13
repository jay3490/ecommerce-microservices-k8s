using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Notification.Worker.Data;
using Notification.Worker.Email;
using Notification.Worker.Messaging;

namespace Notification.Tests;

public sealed class NotificationHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly RecordingEmailSender _email = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly Guid _orderId = Guid.Parse("4d2c9a1e-0000-4000-8000-000000000001");

    public NotificationHandlerTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Registration_stores_the_recipient_and_sends_a_welcome_email()
    {
        await HandleAsync((h, c) => h.HandleAsync(new UserRegistered("user-1", "alice@example.com", "Alice"), c, CancellationToken.None));

        var sent = Assert.Single(_email.Sent);
        Assert.Equal("alice@example.com", sent.To);
        Assert.Contains("Hello Alice", sent.Body);
        await using var db = CreateDb();
        Assert.Equal("alice@example.com", (await db.Recipients.SingleAsync()).Email);
        Assert.Equal("Welcome", (await db.SentNotifications.SingleAsync()).Kind);
    }

    [Fact]
    public async Task A_confirmed_order_is_announced_with_its_total()
    {
        await RegisterAsync();

        await HandleAsync((h, c) => h.HandleAsync(new OrderConfirmed(_orderId, "user-1", 217.5m, "USD"), c, CancellationToken.None));

        var sent = _email.Sent.Last();
        Assert.Equal("Order 4D2C9A1E confirmed", sent.Subject);
        Assert.Contains("217.50 USD", sent.Body);
        await using var db = CreateDb();
        Assert.Equal(_orderId, (await db.SentNotifications.SingleAsync(s => s.Kind == "OrderConfirmed")).OrderId);
    }

    [Fact]
    public async Task A_cancelled_order_tells_the_customer_why()
    {
        await RegisterAsync();

        await HandleAsync((h, c) => h.HandleAsync(new OrderCancelled(_orderId, "user-1", "Card declined by issuer."), c, CancellationToken.None));

        Assert.Contains("Reason: Card declined by issuer.", _email.Sent.Last().Body);
    }

    [Fact]
    public async Task Shipping_updates_include_the_carrier_and_tracking_number()
    {
        await RegisterAsync();

        await HandleAsync((h, c) => h.HandleAsync(
            new ShipmentDispatched(_orderId, Guid.NewGuid(), "user-1", "Simulated Post", "SP0000000042"), c, CancellationToken.None));
        await HandleAsync((h, c) => h.HandleAsync(new ShipmentDelivered(_orderId, Guid.NewGuid(), "user-1"), c, CancellationToken.None));

        Assert.Contains("Tracking number: SP0000000042", _email.Sent[^2].Body);
        Assert.Equal("Order 4D2C9A1E delivered", _email.Sent[^1].Subject);
    }

    [Fact]
    public async Task Customers_without_a_known_address_are_skipped()
    {
        await HandleAsync((h, c) => h.HandleAsync(new OrderConfirmed(_orderId, "dev-token-user", 10m, "USD"), c, CancellationToken.None));

        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task A_second_registration_event_updates_the_address()
    {
        await RegisterAsync();

        await HandleAsync((h, c) => h.HandleAsync(new UserRegistered("user-1", "alice@new.example", "Alice B"), c, CancellationToken.None));

        await using var db = CreateDb();
        var recipient = await db.Recipients.SingleAsync();
        Assert.Equal(("alice@new.example", "Alice B"), (recipient.Email, recipient.DisplayName));
    }

    [Fact]
    public async Task If_sending_fails_nothing_is_recorded_so_the_retry_sends_it()
    {
        await RegisterAsync();
        _email.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HandleAsync((h, c) => h.HandleAsync(new OrderConfirmed(_orderId, "user-1", 10m, "USD"), c, CancellationToken.None)));

        await using var db = CreateDb();
        Assert.DoesNotContain(await db.SentNotifications.ToListAsync(), s => s.Kind == "OrderConfirmed");
    }

    public void Dispose() => _connection.Dispose();

    private Task RegisterAsync() =>
        HandleAsync((h, c) => h.HandleAsync(new UserRegistered("user-1", "alice@example.com", "Alice"), c, CancellationToken.None));

    private async Task HandleAsync(Func<NotificationHandlers, MessageContext, Task> handle)
    {
        await using var db = CreateDb();
        var handlers = new NotificationHandlers(db, _email, _clock, NullLogger<NotificationHandlers>.Instance);
        await handle(handlers, new MessageContext(Guid.NewGuid(), "test", null));
        await db.SaveChangesAsync();
    }

    private NotificationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>().UseSqlite(_connection).Options);
}

public sealed class RecordingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public bool Fail { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("SMTP server unavailable");
        }

        Sent.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
