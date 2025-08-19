using System.Threading.RateLimiting;

namespace Gateway.RateLimiting;

/// <summary>
/// A fixed-window limiter for one client whose counter lives in an <see cref="IRateLimitStore"/>
/// (Redis in a multi-replica deployment). The count is only known after an asynchronous round
/// trip, so the synchronous attempt never grants a permit and the rate limiting middleware
/// falls through to <see cref="AcquireAsyncCore"/>, which does the counting.
/// </summary>
public sealed class DistributedFixedWindowRateLimiter(
    IRateLimitStore store, string key, int permitLimit, TimeSpan window, TimeProvider clock) : RateLimiter
{
    private static readonly RateLimitLease Pending = new Lease(false, null);
    private long _lastUsedTicks = clock.GetUtcNow().UtcTicks;
    private long _successful;
    private long _failed;

    public override TimeSpan? IdleDuration => clock.GetUtcNow() - new DateTimeOffset(Interlocked.Read(ref _lastUsedTicks), TimeSpan.Zero);

    public override RateLimiterStatistics GetStatistics() => new()
    {
        TotalSuccessfulLeases = Interlocked.Read(ref _successful),
        TotalFailedLeases = Interlocked.Read(ref _failed),
    };

    protected override RateLimitLease AttemptAcquireCore(int permitCount) => Pending;

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _lastUsedTicks, clock.GetUtcNow().UtcTicks);
        var counted = await store.IncrementAsync(key, window, cancellationToken);
        if (counted.Count <= permitLimit)
        {
            Interlocked.Increment(ref _successful);
            return new Lease(true, null);
        }

        Interlocked.Increment(ref _failed);
        return new Lease(false, counted.ResetsIn);
    }

    private sealed class Lease(bool acquired, TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => acquired;

        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is not null && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = retryAfter.Value;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
