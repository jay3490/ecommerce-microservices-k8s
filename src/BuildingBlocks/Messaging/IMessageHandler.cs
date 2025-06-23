namespace BuildingBlocks.Messaging;

public sealed record MessageContext(Guid MessageId, string MessageType, string? CorrelationId);

/// <summary>
/// Handles one message type. Handlers change tracked entities and enqueue outgoing messages
/// on <see cref="Outbox.IOutbox"/> but do not call <c>SaveChanges</c>: the inbox saves the
/// handler's changes, its outbox rows and the processed-message record in one transaction.
/// </summary>
public interface IMessageHandler<in TMessage> where TMessage : class, IIntegrationMessage
{
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken cancellationToken);
}
