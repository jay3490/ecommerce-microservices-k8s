using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Messaging.RabbitMq;

internal sealed class RabbitMqHealthCheck(RabbitMqConnectionProvider connections) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(connections.IsConnected
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Not connected to RabbitMQ"));
}
