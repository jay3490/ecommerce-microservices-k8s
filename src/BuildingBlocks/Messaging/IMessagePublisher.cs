namespace BuildingBlocks.Messaging;

public sealed record OutgoingMessage(
    Guid MessageId,
    string MessageType,
    string Payload,
    string? CorrelationId,
    string? TraceParent);

/// <summary>
/// Sends an already serialized message to the broker. Only the outbox dispatcher calls this;
/// application code enqueues messages on the outbox instead.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>Completes once the broker has confirmed the message; throws otherwise.</summary>
    Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken);
}
