using System.Collections.Concurrent;
using StackExchange.Redis;

namespace Gateway.RateLimiting;

/// <summary>The state of one client's current window after counting a request.</summary>
public readonly record struct WindowCount(long Count, TimeSpan ResetsIn);

/// <summary>Counts requests per key in fixed windows.</summary>
public interface IRateLimitStore
{
    /// <summary>Adds one request to the key's current window, starting a window if none is open.</summary>
    Task<WindowCount> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken);
}

/// <summary>
/// Counts in this process only. Used when no Redis is configured (one replica, local runs)
/// and as the fallback while Redis is unreachable.
/// </summary>
public sealed class InMemoryRateLimitStore(TimeProvider clock) : IRateLimitStore
{
    private readonly ConcurrentDictionary<string, Window> _windows = new();

    public Task<WindowCount> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var current = _windows.AddOrUpdate(
            key,
            _ => new Window(now + window, 1),
            (_, existing) => existing.EndsAt <= now ? new Window(now + window, 1) : existing with { Count = existing.Count + 1 });

        // Drop expired windows now and then so the dictionary does not grow without bound.
        if (_windows.Count > 10_000)
        {
            foreach (var expired in _windows.Where(entry => entry.Value.EndsAt <= now))
            {
                _windows.TryRemove(expired);
            }
        }

        return Task.FromResult(new WindowCount(current.Count, current.EndsAt - now));
    }

    private sealed record Window(DateTimeOffset EndsAt, long Count);
}

/// <summary>
/// Counts in Redis, so every gateway replica enforces the same limit. INCR and the expiry run
/// in one Lua script, which Redis executes atomically. If Redis cannot be reached the request
/// is counted locally instead: limits then apply per replica until Redis is back, rather than
/// failing requests or switching limiting off.
/// </summary>
public sealed class RedisRateLimitStore(
    IConnectionMultiplexer redis,
    InMemoryRateLimitStore fallback,
    TimeProvider clock,
    ILogger<RedisRateLimitStore> logger) : IRateLimitStore
{
    private const string Script = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then
          redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 then
          redis.call('PEXPIRE', KEYS[1], ARGV[1])
          ttl = tonumber(ARGV[1])
        end
        return { count, ttl }
        """;

    private static readonly TimeSpan WarningInterval = TimeSpan.FromSeconds(30);
    private long _lastWarningTicks;

    public async Task<WindowCount> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken)
    {
        try
        {
            var result = (RedisResult[])(await redis.GetDatabase().ScriptEvaluateAsync(
                Script, [new RedisKey("ratelimit:" + key)], [(long)window.TotalMilliseconds]))!;
            return new WindowCount((long)result[0], TimeSpan.FromMilliseconds((long)result[1]));
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or TimeoutException)
        {
            WarnThrottled(ex);
            return await fallback.IncrementAsync(key, window, cancellationToken);
        }
    }

    private void WarnThrottled(Exception ex)
    {
        var now = clock.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _lastWarningTicks);
        if (now - last >= WarningInterval.Ticks && Interlocked.CompareExchange(ref _lastWarningTicks, now, last) == last)
        {
            logger.LogWarning(ex, "Redis unavailable for rate limiting; counting per replica until it is back");
        }
    }
}
