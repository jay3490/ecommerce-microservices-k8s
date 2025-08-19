using System.Globalization;
using System.Threading.RateLimiting;
using Gateway.Auth;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Gateway.RateLimiting;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Name referenced by the routes' <c>RateLimiterPolicy</c>.</summary>
    public const string PerClientPolicy = "per-client";

    public int PermitLimit { get; set; } = 100;
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);
}

public static class RateLimitingExtensions
{
    /// <summary>
    /// Fixed-window limit per client: per authenticated subject, otherwise per client IP.
    /// With a Redis connection (<c>ConnectionStrings:Redis</c>) the counters are shared by all
    /// gateway replicas; without one each replica counts on its own.
    /// </summary>
    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .Validate(o => o.PermitLimit > 0 && o.Window > TimeSpan.Zero, "RateLimiting needs a positive PermitLimit and Window.")
            .ValidateOnStart();

        services.AddSingleton<InMemoryRateLimitStore>();
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Redis")))
        {
            services.AddSingleton<IRateLimitStore>(provider => provider.GetRequiredService<InMemoryRateLimitStore>());
        }
        else
        {
            services.AddSingleton<IRateLimitStore>(provider => new RedisRateLimitStore(
                provider.GetRequiredService<IConnectionMultiplexer>(),
                provider.GetRequiredService<InMemoryRateLimitStore>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<RedisRateLimitStore>>()));
        }

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            limiter.AddPolicy(RateLimitingOptions.PerClientPolicy, httpContext =>
            {
                var services = httpContext.RequestServices;
                var settings = services.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                var subject = httpContext.User.FindFirst(AuthPolicies.SubjectClaim)?.Value;
                var key = subject is not null
                    ? $"sub:{subject}"
                    : $"ip:{httpContext.Connection.RemoteIpAddress}";

                return RateLimitPartition.Get(key, partitionKey => new DistributedFixedWindowRateLimiter(
                    services.GetRequiredService<IRateLimitStore>(),
                    partitionKey,
                    settings.PermitLimit,
                    settings.Window,
                    services.GetRequiredService<TimeProvider>()));
            });
        });

        return services;
    }
}
