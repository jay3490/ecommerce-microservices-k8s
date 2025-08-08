using System.Diagnostics.Metrics;
using BuildingBlocks.Observability;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ordering.Api.Data;
using Serilog.Context;

namespace Ordering.Api.Saga;

public sealed class SagaOptions
{
    public const string SectionName = "Saga";

    /// <summary>How long the saga waits for a reply before it gives the step up.</summary>
    public TimeSpan StepTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often stuck sagas are looked for.</summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(30);

    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// Finds sagas that have been waiting in one state for longer than the step timeout and
/// applies <see cref="OrderSaga.TimeOut"/> to each. Every saga is committed on its own, with
/// the commands it sends, and the saga row version makes a timeout lose cleanly against a
/// reply that is processed at the same moment (or against another replica's scan).
/// </summary>
public sealed class SagaTimeoutProcessor(
    OrderingDbContext db,
    IOutbox outbox,
    TimeProvider clock,
    IOptions<SagaOptions> options,
    ILogger<SagaTimeoutProcessor> logger)
{
    private static readonly Counter<long> TimedOut =
        Telemetry.Meter.CreateCounter<long>("ordering.saga.timeouts", description: "Saga steps given up or retried after a timeout");

    /// <returns>The number of sagas that were timed out.</returns>
    public async Task<int> ProcessAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var cutoff = now - settings.StepTimeout;

        var expired = await db.OrderSagas.AsNoTracking()
            .Where(s => s.State != OrderSagaState.Completed && s.State != OrderSagaState.Cancelled && s.UpdatedAt < cutoff)
            .OrderBy(s => s.UpdatedAt)
            .Select(s => s.OrderId)
            .Take(settings.BatchSize)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var orderId in expired)
        {
            CorrelationContext.Current = CorrelationContext.NewId();
            using (LogContext.PushProperty(CorrelationContext.LogPropertyName, CorrelationContext.Current))
            {
                if (await TimeOutAsync(orderId, now, cutoff, cancellationToken))
                {
                    processed++;
                }
            }
        }

        return processed;
    }

    private async Task<bool> TimeOutAsync(Guid orderId, DateTimeOffset now, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var saga = await db.OrderSagas.FirstOrDefaultAsync(s => s.OrderId == orderId, cancellationToken);
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        // A reply may have moved the saga on since the scan.
        if (saga is null || order is null || saga.IsFinished || saga.UpdatedAt >= cutoff)
        {
            return false;
        }

        var from = saga.State;
        var step = saga.TimeOut(order, now);
        if (!step.Applied)
        {
            return false;
        }

        foreach (var message in step.Messages)
        {
            outbox.Enqueue(message);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("Saga for order {OrderId} changed while timing out; leaving it to the next scan", orderId);
            return false;
        }

        TimedOut.Add(1, new KeyValuePair<string, object?>("state", from.ToString()));
        logger.LogWarning("Saga for order {OrderId} timed out in {From} (timeout {Count}); now {To}, sent {Messages}",
            orderId, from, saga.Timeouts, saga.State, string.Join(", ", step.Messages.Select(m => m.GetType().Name)));
        return true;
    }
}

/// <summary>Runs <see cref="SagaTimeoutProcessor"/> on a fixed interval.</summary>
public sealed class SagaTimeoutService(
    IServiceScopeFactory scopeFactory,
    IOptions<SagaOptions> options,
    ILogger<SagaTimeoutService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.ScanInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ScanAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ScanAsync(CancellationToken stoppingToken)
    {
        try
        {
            int processed;
            do
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                processed = await scope.ServiceProvider.GetRequiredService<SagaTimeoutProcessor>().ProcessAsync(stoppingToken);
            }
            while (processed >= options.Value.BatchSize && !stoppingToken.IsCancellationRequested);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Saga timeout scan failed");
        }
    }
}
