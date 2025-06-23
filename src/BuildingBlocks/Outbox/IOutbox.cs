using BuildingBlocks.Messaging;

namespace BuildingBlocks.Outbox;

public interface IOutbox
{
    /// <summary>
    /// Adds the message to the current unit of work. Nothing is sent until the caller's
    /// <c>SaveChanges</c> commits; the dispatcher publishes it afterwards.
    /// </summary>
    void Enqueue<TMessage>(TMessage message) where TMessage : class, IIntegrationMessage;
}
