using System.Globalization;
using System.Threading.RateLimiting;
using Gateway.Auth;
using Microsoft.Extensions.Options;

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
    /// Limits are kept in memory, so with several gateway replicas each enforces its own.
    /// </summary>
    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>().Bind(configuration.GetSection(RateLimitingOptions.SectionName));

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
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                var subject = httpContext.User.FindFirst(AuthPolicies.SubjectClaim)?.Value;
                var key = subject is not null
                    ? $"sub:{subject}"
                    : $"ip:{httpContext.Connection.RemoteIpAddress}";

                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.PermitLimit,
                    Window = settings.Window,
                    QueueLimit = 0,
                });
            });
        });

        return services;
    }
}
