using System.Net;
using System.Threading.RateLimiting;
using Gateway.RateLimiting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Gateway.Tests;

/// <summary>Stands in for Redis: one counter store shared by several gateway instances.</summary>
public sealed class SharedCounterStore : IRateLimitStore
{
    private readonly InMemoryRateLimitStore _inner;

    public SharedCounterStore(TimeProvider clock) => _inner = new InMemoryRateLimitStore(clock);

    public int Calls { get; private set; }

    public Task<WindowCount> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken)
    {
        Calls++;
        return _inner.IncrementAsync(key, window, cancellationToken);
    }
}

/// <summary>A gateway replica whose rate limit counters live in the given store.</summary>
public sealed class ReplicaFactory(IRateLimitStore store) : GatewayFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRateLimitStore>();
            services.AddSingleton(store);
        });
    }
}

public sealed class DistributedRateLimitingTests
{
    private static readonly DateTimeOffset Start = new(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Replicas_sharing_a_store_enforce_one_limit_per_client()
    {
        var store = new SharedCounterStore(TimeProvider.System);
        using var replicaA = new ReplicaFactory(store);
        using var replicaB = new ReplicaFactory(store);
        using var onA = await replicaA.CreateClientWithTokenAsync("customer-7", "customer");
        using var onB = await replicaB.CreateClientWithTokenAsync("customer-7", "customer");

        // PermitLimit is 3 per minute: two requests on one replica, one on the other...
        Assert.Equal(HttpStatusCode.BadGateway, (await onA.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, (await onA.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, (await onB.GetAsync("/api/orders")).StatusCode);

        // ...and the fourth is refused wherever it lands.
        var limitedOnB = await onB.GetAsync("/api/orders");
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedOnB.StatusCode);
        Assert.NotNull(limitedOnB.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await onA.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task Without_a_shared_store_each_replica_counts_on_its_own()
    {
        using var replicaA = new GatewayFactory();
        using var replicaB = new GatewayFactory();
        using var onA = await replicaA.CreateClientWithTokenAsync("customer-8", "customer");
        using var onB = await replicaB.CreateClientWithTokenAsync("customer-8", "customer");

        for (var i = 0; i < GatewayFactory.PermitLimit; i++)
        {
            await onA.GetAsync("/api/orders");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await onA.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, (await onB.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task The_limiter_grants_up_to_the_limit_then_reports_when_the_window_resets()
    {
        var clock = new MutableClock(Start);
        var limiter = new DistributedFixedWindowRateLimiter(new InMemoryRateLimitStore(clock), "sub:x", 2, TimeSpan.FromSeconds(10), clock);

        Assert.False(limiter.AttemptAcquire().IsAcquired);
        Assert.True((await limiter.AcquireAsync()).IsAcquired);
        clock.Now += TimeSpan.FromSeconds(4);
        Assert.True((await limiter.AcquireAsync()).IsAcquired);
        using var rejected = await limiter.AcquireAsync();

        Assert.False(rejected.IsAcquired);
        Assert.True(rejected.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(6), retryAfter);
        Assert.Equal(2, limiter.GetStatistics()!.TotalSuccessfulLeases);
        Assert.Equal(1, limiter.GetStatistics()!.TotalFailedLeases);

        clock.Now += TimeSpan.FromSeconds(6);
        Assert.True((await limiter.AcquireAsync()).IsAcquired);
    }

    [Fact]
    public async Task Counters_are_kept_per_key()
    {
        var store = new InMemoryRateLimitStore(new MutableClock(Start));

        await store.IncrementAsync("a", TimeSpan.FromMinutes(1), CancellationToken.None);
        var a = await store.IncrementAsync("a", TimeSpan.FromMinutes(1), CancellationToken.None);
        var b = await store.IncrementAsync("b", TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Equal((2, 1), (a.Count, b.Count));
    }

    [Fact]
    public async Task When_redis_is_unreachable_requests_are_counted_locally_instead()
    {
        using var redis = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:1,abortConnect=false,connectTimeout=200,asyncTimeout=500");
        var clock = new MutableClock(Start);
        var store = new RedisRateLimitStore(redis, new InMemoryRateLimitStore(clock), clock, NullLogger<RedisRateLimitStore>.Instance);

        var first = await store.IncrementAsync("sub:y", TimeSpan.FromMinutes(1), CancellationToken.None);
        var second = await store.IncrementAsync("sub:y", TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Equal((1, 2), (first.Count, second.Count));
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
