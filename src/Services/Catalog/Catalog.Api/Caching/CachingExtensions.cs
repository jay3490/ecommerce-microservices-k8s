using BuildingBlocks.Health;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Catalog.Api.Caching;

public static class CachingExtensions
{
    /// <summary>
    /// Uses Redis when <c>ConnectionStrings:Redis</c> is set, otherwise an in-process cache so
    /// the service can run on its own during development.
    /// </summary>
    public static WebApplicationBuilder AddProductCaching(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<ProductCacheOptions>(builder.Configuration.GetSection(ProductCacheOptions.SectionName));
        builder.Services.AddScoped<ProductCache>();

        var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            builder.Services.AddDistributedMemoryCache();
            return builder;
        }

        // One multiplexer shared by the cache and the health check. AbortOnConnectFail=false
        // lets the service start while Redis is down and reconnect in the background.
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var configuration = ConfigurationOptions.Parse(redisConnectionString);
            configuration.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(configuration);
        });

        builder.Services.AddStackExchangeRedisCache(options => options.InstanceName = "catalog:");
        builder.Services.AddOptions<RedisCacheOptions>()
            .Configure<IConnectionMultiplexer>((options, multiplexer) =>
                options.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer));

        builder.Services.AddHealthChecks()
            .AddCheck<RedisHealthCheck>("redis", failureStatus: HealthStatus.Degraded, tags: [HealthTags.Ready]);

        return builder;
    }
}

internal sealed class RedisHealthCheck(IConnectionMultiplexer multiplexer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var latency = await multiplexer.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Ping {latency.TotalMilliseconds:F1} ms");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Redis is unreachable", ex);
        }
    }
}
