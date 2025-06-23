using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Publishes pending outbox messages in the order they were written. Delivery is
/// at-least-once: if the process dies between publishing and recording the result, the
/// message is published again and the consumer's inbox discards the duplicate.
/// </summary>
public sealed class OutboxDispatcher<TDbContext>(
    TDbContext db,
    IMessagePublisher publisher,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxDispatcher<TDbContext>> logger)
    where TDbContext : DbContext
{
    private const int MaxErrorLength = 2000;

    /// <returns>The number of messages published.</returns>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var batch = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null && m.Attempts < settings.MaxAttempts)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(settings.BatchSize)
            .ToListAsync(cancellationToken);

        var published = 0;
        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishAsync(
                    new OutgoingMessage(message.Id, message.Type, message.Payload, message.CorrelationId, message.TraceParent),
                    cancellationToken);

                message.ProcessedOnUtc = clock.GetUtcNow().UtcDateTime;
                message.LastError = null;
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > MaxErrorLength ? ex.Message[..MaxErrorLength] : ex.Message;

                if (message.Attempts >= settings.MaxAttempts)
                {
                    logger.LogError(ex, "Giving up on outbox message {MessageId} ({MessageType}) after {Attempts} attempts",
                        message.Id, message.Type, message.Attempts);
                }
                else
                {
                    logger.LogWarning(ex, "Publishing outbox message {MessageId} ({MessageType}) failed, attempt {Attempts}",
                        message.Id, message.Type, message.Attempts);
                }

                // Stop here so later messages are not published ahead of this one.
                break;
            }
        }

        // Record what was published even if shutdown has been requested meanwhile, otherwise
        // every message in the batch would be sent again on the next start.
        await db.SaveChangesAsync(CancellationToken.None);
        return published;
    }
}
