using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Inbox;

/// <summary>
/// Inbox stored in the service's own database. The handler's entity changes, any outbox
/// messages it enqueued and the inbox record are written by a single <c>SaveChanges</c>, so
/// either all of them are committed or none are.
/// </summary>
public sealed class EntityFrameworkInbox<TDbContext>(TDbContext db, TimeProvider clock) : IInbox
    where TDbContext : DbContext
{
    public async Task<bool> TryProcessAsync(
        Guid messageId,
        string messageType,
        string consumer,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        if (await ExistsAsync(messageId, consumer, cancellationToken))
        {
            return false;
        }

        await handler(cancellationToken);

        db.Set<InboxMessage>().Add(new InboxMessage
        {
            MessageId = messageId,
            Consumer = consumer,
            MessageType = messageType,
            ProcessedOnUtc = clock.GetUtcNow().UtcDateTime,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Two deliveries of the same message raced each other (for example on two replicas)
            // and the other one committed first: the primary key rejected this insert.
            if (await ExistsAsync(messageId, consumer, CancellationToken.None))
            {
                return false;
            }

            throw;
        }
    }

    private Task<bool> ExistsAsync(Guid messageId, string consumer, CancellationToken cancellationToken) =>
        db.Set<InboxMessage>()
            .AsNoTracking()
            .AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, cancellationToken);
}
