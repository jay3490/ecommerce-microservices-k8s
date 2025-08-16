using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Outbox;

public sealed record StuckOutboxMessage(Guid Id, string Type, string? CorrelationId, DateTime OccurredOnUtc, int Attempts, string? LastError);

/// <summary>
/// Operator endpoints for outbox messages the dispatcher gave up on after
/// <see cref="OutboxOptions.MaxAttempts"/> failed publishes. Retrying resets the attempt count
/// so the dispatcher picks the message up again on its next pass.
/// </summary>
public static class OutboxAdminEndpoints
{
    public static IEndpointRouteBuilder MapOutboxAdminEndpoints<TDbContext>(this IEndpointRouteBuilder endpoints)
        where TDbContext : DbContext
    {
        var group = endpoints.MapGroup("/admin/outbox").WithTags("Outbox").ExcludeFromDescription();

        // The DbContext is resolved by hand: route handlers with a generic parameter type
        // trip the ASP.NET Core route handler analyzer.
        group.MapGet("/failed", async (HttpContext context, IOptions<OutboxOptions> options, CancellationToken cancellationToken) =>
            Results.Ok(await StuckMessages(context.RequestServices.GetRequiredService<TDbContext>(), options.Value)
                .OrderBy(m => m.OccurredOnUtc)
                .Take(200)
                .Select(m => new StuckOutboxMessage(m.Id, m.Type, m.CorrelationId, m.OccurredOnUtc, m.Attempts, m.LastError))
                .ToListAsync(cancellationToken)));

        group.MapPost("/failed/retry", async (HttpContext context, IOptions<OutboxOptions> options, OutboxSignal signal, CancellationToken cancellationToken) =>
        {
            var db = context.RequestServices.GetRequiredService<TDbContext>();
            var reset = await StuckMessages(db, options.Value)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.Attempts, 0), cancellationToken);
            signal.Notify();
            return Results.Ok(new { reset });
        });

        return endpoints;
    }

    private static IQueryable<OutboxMessage> StuckMessages(DbContext db, OutboxOptions options) =>
        db.Set<OutboxMessage>().Where(m => m.ProcessedOnUtc == null && m.Attempts >= options.MaxAttempts);
}
