using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.OutputCaching.StackExchangeRedis;
using StackExchange.Redis;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Gateway.Caching;

public sealed class CatalogCacheOptions
{
    public const string SectionName = "Gateway:CatalogCache";

    /// <summary>Name referenced by the catalog read route's <c>OutputCachePolicy</c>.</summary>
    public const string PolicyName = "catalog";

    public const string Tag = "catalog";

    /// <summary>How long a catalog response is served from the cache.</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Output caching for anonymous catalog reads. The default output cache rules apply: only
/// GET and HEAD, only 200 responses, never requests with an Authorization header or
/// responses that set cookies. Entries vary by the whole query string. A successful product
/// change through the gateway evicts every cached catalog response. With Redis the cache is
/// shared by all replicas; otherwise each replica caches in memory.
/// </summary>
public static class CatalogCaching
{
    public const string WriteRouteId = "catalog-write";

    public static IServiceCollection AddCatalogCaching(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(CatalogCacheOptions.SectionName).Get<CatalogCacheOptions>() ?? new CatalogCacheOptions();

        services.AddOutputCache(options => options.AddPolicy(CatalogCacheOptions.PolicyName, policy => policy
            .Expire(settings.Duration)
            .SetVaryByQuery("*")
            .Tag(CatalogCacheOptions.Tag)));

        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("Redis")))
        {
            services.AddStackExchangeRedisOutputCache(options => options.InstanceName = "gateway:output:");
            services.AddOptions<RedisOutputCacheOptions>()
                .Configure<IConnectionMultiplexer>((options, multiplexer) =>
                    options.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer));
        }

        return services;
    }

    /// <summary>Evicts cached catalog reads after a successful write through the gateway.</summary>
    public static void AddCatalogEviction(TransformBuilderContext context)
    {
        if (context.Route.RouteId != WriteRouteId)
        {
            return;
        }

        context.AddResponseTransform(async transform =>
        {
            if (transform.ProxyResponse is { IsSuccessStatusCode: true })
            {
                var store = transform.HttpContext.RequestServices.GetRequiredService<IOutputCacheStore>();
                await store.EvictByTagAsync(CatalogCacheOptions.Tag, transform.HttpContext.RequestAborted);
            }
        });
    }
}
