using Basket.Api.Baskets;
using Basket.Api.Catalog;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("basket-api");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<BasketOptions>(builder.Configuration.GetSection(BasketOptions.SectionName));

var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
                            ?? throw new InvalidOperationException("ConnectionStrings:Redis is not configured.");
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    // Start even while Redis is down; the readiness probe reports it until it reconnects.
    var configuration = ConfigurationOptions.Parse(redisConnectionString);
    configuration.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(configuration);
});
builder.Services.AddSingleton<IBasketStore, RedisBasketStore>();
builder.Services.AddScoped<BasketCheckout>();
builder.Services.AddCatalogPricing(builder.Configuration);

// Basket keeps no database, so it publishes checkouts directly (with broker confirms)
// instead of through an outbox, and consumes nothing.
builder.Services.AddMessaging(builder.Configuration, queueName: "basket");

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<RedisHealthCheck>("redis", tags: [HealthTags.Ready]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseServiceDefaults();
app.MapBasketEndpoints();

await app.RunAsync();

public partial class Program;

internal sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var latency = await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Ping {latency.TotalMilliseconds:F1} ms");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis is unreachable", ex);
        }
    }
}
