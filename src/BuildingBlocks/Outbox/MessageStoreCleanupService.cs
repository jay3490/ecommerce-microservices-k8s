using BuildingBlocks.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Deletes processed outbox and inbox rows past the retention period. Inbox rows must be
/// kept longer than any realistic redelivery delay, otherwise duplicates are no longer caught.
/// </summary>
public sealed class MessageStoreCleanupService<TDbContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<MessageStoreCleanupService<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CleanupInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CleanupAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
            var cutoff = clock.GetUtcNow().UtcDateTime - options.Value.Retention;

            var outbox = await db.Set<OutboxMessage>()
                .Where(m => m.ProcessedOnUtc != null && m.ProcessedOnUtc < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
            var inbox = await db.Set<InboxMessage>()
                .Where(m => m.ProcessedOnUtc < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            if (outbox + inbox > 0)
            {
                logger.LogInformation("Removed {Outbox} outbox and {Inbox} inbox rows older than {Cutoff}", outbox, inbox, cutoff);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Message store cleanup failed");
        }
    }
}
