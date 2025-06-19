using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace BuildingBlocks.Persistence;

public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Applies pending EF Core migrations at startup, retrying while the database is still
    /// starting (common under docker compose). Disable with <c>Database:MigrateOnStartup=false</c>.
    /// </summary>
    public static async Task MigrateDatabaseAsync<TContext>(this WebApplication app)
        where TContext : DbContext
    {
        if (!app.Configuration.GetValue("Database:MigrateOnStartup", true))
        {
            return;
        }

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrationExtensions));

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 10,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Linear,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => ex is not OperationCanceledException),
                OnRetry = args =>
                {
                    logger.LogWarning(args.Outcome.Exception,
                        "Database for {DbContext} not reachable yet (attempt {Attempt}), retrying in {Delay}",
                        typeof(TContext).Name, args.AttemptNumber + 1, args.RetryDelay);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();

        await pipeline.ExecuteAsync(async cancellationToken =>
        {
            await using var scope = app.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.MigrateAsync(cancellationToken);
        }, app.Lifetime.ApplicationStopping);

        logger.LogInformation("Database migrations applied for {DbContext}", typeof(TContext).Name);
    }
}
