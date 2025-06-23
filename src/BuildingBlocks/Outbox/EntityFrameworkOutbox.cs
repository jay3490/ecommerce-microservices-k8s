using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Outbox;

public sealed class EntityFrameworkOutbox<TDbContext>(TDbContext db, OutboxSignal signal, TimeProvider clock) : IOutbox
    where TDbContext : DbContext
{
    private bool _wakeDispatcherOnSave;

    public void Enqueue<TMessage>(TMessage message) where TMessage : class, IIntegrationMessage
    {
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = MessageType.NameOf(message),
            Payload = JsonSerializer.Serialize(message, message.GetType(), MessagingJson.Options),
            CorrelationId = CorrelationContext.Current,
            TraceParent = Activity.Current?.Id,
            OccurredOnUtc = clock.GetUtcNow().UtcDateTime,
        });

        // Wake the dispatcher as soon as the transaction commits instead of waiting for its
        // next polling interval.
        if (!_wakeDispatcherOnSave)
        {
            db.SavedChanges += (_, _) => signal.Notify();
            _wakeDispatcherOnSave = true;
        }
    }
}
