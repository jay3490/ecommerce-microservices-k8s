using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging.DeadLetters;

public sealed record DeadLetterRequest(IReadOnlyList<string>? MessageIds, bool All = false, int Limit = DeadLetterService.DefaultLimit);

/// <summary>
/// Operator endpoints for the service's dead-letter queue. They are meant to be reached
/// only through the gateway's admin routes (<c>/api/admin/{service}/dead-letters</c>).
/// </summary>
public static class DeadLetterEndpoints
{
    public static IEndpointRouteBuilder MapDeadLetterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/dead-letters").WithTags("Dead letters").ExcludeFromDescription();

        group.MapGet("/", async (DeadLetterService deadLetters, CancellationToken cancellationToken, int limit = DeadLetterService.DefaultLimit) =>
            Results.Ok(new
            {
                queue = deadLetters.QueueName,
                messages = await deadLetters.ListAsync(limit, cancellationToken),
            }));

        group.MapPost("/replay", async (DeadLetterRequest request, DeadLetterService deadLetters, CancellationToken cancellationToken) =>
        {
            if (!request.All && request.MessageIds is not { Count: > 0 })
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["MessageIds"] = ["Give the ids of the messages to replay, or set all to true."],
                });
            }

            var ids = request.All ? null : request.MessageIds!.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return Results.Ok(await deadLetters.ReplayAsync(ids, request.Limit, cancellationToken));
        });

        group.MapPost("/discard", async (DeadLetterRequest request, DeadLetterService deadLetters, CancellationToken cancellationToken) =>
        {
            if (request.MessageIds is not { Count: > 0 })
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["MessageIds"] = ["Give the ids of the messages to discard."],
                });
            }

            var ids = request.MessageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return Results.Ok(await deadLetters.DiscardAsync(ids, request.Limit, cancellationToken));
        });

        return endpoints;
    }

    public static IServiceCollection AddDeadLetterTooling(this IServiceCollection services)
    {
        services.AddSingleton<IDeadLetterQueue, RabbitMqDeadLetterQueue>();
        services.AddSingleton<DeadLetterService>();
        return services;
    }
}
