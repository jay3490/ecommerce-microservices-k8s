using System.Diagnostics.Metrics;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Observability;
using Microsoft.EntityFrameworkCore;
using Notification.Worker.Data;
using Notification.Worker.Email;

namespace Notification.Worker.Messaging;

/// <summary>
/// Turns order and shipping events into emails. The email is sent before the inbox commits,
/// so a crash between the two can send it twice: delivery is at-least-once, which is the
/// usual trade-off for a side effect outside the database.
/// </summary>
public sealed class NotificationHandlers(
    NotificationDbContext db,
    IEmailSender email,
    TimeProvider clock,
    ILogger<NotificationHandlers> logger) :
    IMessageHandler<UserRegistered>,
    IMessageHandler<OrderConfirmed>,
    IMessageHandler<OrderCancelled>,
    IMessageHandler<ShipmentDispatched>,
    IMessageHandler<ShipmentDelivered>
{
    private static readonly Counter<long> Sent =
        Telemetry.Meter.CreateCounter<long>("notifications.sent", description: "Emails handed to the sender, by kind");

    public async Task HandleAsync(UserRegistered message, MessageContext context, CancellationToken cancellationToken)
    {
        var recipient = await db.Recipients.FindAsync([message.UserId], cancellationToken);
        if (recipient is null)
        {
            recipient = new Recipient { UserId = message.UserId, RegisteredAt = clock.GetUtcNow() };
            db.Recipients.Add(recipient);
        }

        recipient.Email = message.Email;
        recipient.DisplayName = message.DisplayName;

        await SendAsync(recipient, "Welcome", NotificationTemplates.Welcome(recipient), null, context, cancellationToken);
    }

    public Task HandleAsync(OrderConfirmed message, MessageContext context, CancellationToken cancellationToken) =>
        NotifyAsync(message.CustomerId, "OrderConfirmed", message.OrderId, context,
            recipient => NotificationTemplates.OrderConfirmed(recipient, message), cancellationToken);

    public Task HandleAsync(OrderCancelled message, MessageContext context, CancellationToken cancellationToken) =>
        NotifyAsync(message.CustomerId, "OrderCancelled", message.OrderId, context,
            recipient => NotificationTemplates.OrderCancelled(recipient, message), cancellationToken);

    public Task HandleAsync(ShipmentDispatched message, MessageContext context, CancellationToken cancellationToken) =>
        NotifyAsync(message.CustomerId, "ShipmentDispatched", message.OrderId, context,
            recipient => NotificationTemplates.ShipmentDispatched(recipient, message), cancellationToken);

    public Task HandleAsync(ShipmentDelivered message, MessageContext context, CancellationToken cancellationToken) =>
        NotifyAsync(message.CustomerId, "ShipmentDelivered", message.OrderId, context,
            recipient => NotificationTemplates.ShipmentDelivered(recipient, message), cancellationToken);

    private async Task NotifyAsync(
        string customerId,
        string kind,
        Guid orderId,
        MessageContext context,
        Func<Recipient, EmailMessage> template,
        CancellationToken cancellationToken)
    {
        var recipient = await db.Recipients.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == customerId, cancellationToken);
        if (recipient is null)
        {
            // For example a customer signed in with a dev token, who never registered.
            logger.LogInformation("No email address for customer {CustomerId}; skipping {Kind} for order {OrderId}",
                customerId, kind, orderId);
            return;
        }

        await SendAsync(recipient, kind, template(recipient), orderId, context, cancellationToken);
    }

    private async Task SendAsync(
        Recipient recipient, string kind, EmailMessage message, Guid? orderId, MessageContext context, CancellationToken cancellationToken)
    {
        await email.SendAsync(message, cancellationToken);

        db.SentNotifications.Add(new SentNotification
        {
            Id = Guid.NewGuid(),
            MessageId = context.MessageId,
            UserId = recipient.UserId,
            Kind = kind,
            To = message.To,
            Subject = message.Subject,
            OrderId = orderId,
            SentAt = clock.GetUtcNow(),
        });

        Sent.Add(1, new KeyValuePair<string, object?>("kind", kind));
        logger.LogInformation("Sent {Kind} email to customer {CustomerId}", kind, recipient.UserId);
    }
}
