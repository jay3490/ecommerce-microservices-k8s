using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Runs the dispatcher whenever a transaction with outbox messages commits, and on a polling
/// interval as a fallback (for messages written by another replica or left by a failure).
/// </summary>
public sealed class OutboxDispatcherService<TDbContext>(
    IServiceScopeFactory scopeFactory,
    OutboxSignal signal,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcherService<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher<TDbContext>>();
                published = await dispatcher.DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch pass failed");
            }

            // A full batch probably means more is waiting, so go again straight away.
            if (published < options.Value.BatchSize)
            {
                await signal.WaitAsync(options.Value.PollingInterval, stoppingToken);
            }
        }
    }
}
