using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Observability;

namespace BuildingBlocks.Messaging;

public static class MessagePublisherExtensions
{
    /// <summary>
    /// Publishes a message straight to the broker, for services without a database (and so
    /// without an outbox). The call completes once the broker has confirmed the message, so
    /// the caller can make its own state change depend on that.
    /// </summary>
    public static Task PublishAsync<TMessage>(this IMessagePublisher publisher, TMessage message, CancellationToken cancellationToken)
        where TMessage : class, IIntegrationMessage =>
        publisher.PublishAsync(
            new OutgoingMessage(
                Guid.NewGuid(),
                MessageType.NameOf(message),
                JsonSerializer.Serialize(message, message.GetType(), MessagingJson.Options),
                CorrelationContext.Current,
                Activity.Current?.Id),
            cancellationToken);
}
