using System.Diagnostics.Metrics;
using System.Text.Json;
using BuildingBlocks.Observability;
using Catalog.Api.Data;
using Catalog.Api.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Catalog.Api.Caching;

public sealed class ProductCacheOptions
{
    public const string SectionName = "Catalog:Cache";

    public TimeSpan TimeToLive { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Cache-aside reads for products: look in the distributed cache first, fall back to the
/// database on a miss and populate the cache with the result. Writes go to the database and
/// then evict the entry. Cache failures are logged and treated as misses so that an
/// unavailable Redis slows reads down instead of failing them.
/// </summary>
public sealed class ProductCache(
    IDistributedCache cache,
    CatalogDbContext db,
    IOptions<ProductCacheOptions> options,
    ILogger<ProductCache> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly Counter<long> Lookups =
        Telemetry.Meter.CreateCounter<long>("catalog.product_cache.lookups", description: "Product cache lookups by result");

    public async Task<ProductDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var cached = await TryReadAsync(id, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductDto(p.Id, p.Sku, p.Name, p.Description, p.Price, p.Currency))
            .FirstOrDefaultAsync(cancellationToken);

        if (product is not null)
        {
            await TryWriteAsync(product, cancellationToken);
        }

        return product;
    }

    public async Task<IReadOnlyList<ProductDto>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var found = new List<ProductDto>(ids.Count);
        var misses = new List<Guid>();

        foreach (var id in ids)
        {
            var cached = await TryReadAsync(id, cancellationToken);
            if (cached is null)
            {
                misses.Add(id);
            }
            else
            {
                found.Add(cached);
            }
        }

        if (misses.Count > 0)
        {
            var loaded = await db.Products.AsNoTracking()
                .Where(p => misses.Contains(p.Id))
                .Select(p => new ProductDto(p.Id, p.Sku, p.Name, p.Description, p.Price, p.Currency))
                .ToListAsync(cancellationToken);

            foreach (var product in loaded)
            {
                await TryWriteAsync(product, cancellationToken);
            }

            found.AddRange(loaded);
        }

        return found;
    }

    public async Task InvalidateAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(Key(id), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not evict product {ProductId} from the cache", id);
        }
    }

    private async Task<ProductDto?> TryReadAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await cache.GetAsync(Key(id), cancellationToken);
            Lookups.Add(1, new KeyValuePair<string, object?>("result", bytes is null ? "miss" : "hit"));
            return bytes is null ? null : JsonSerializer.Deserialize<ProductDto>(bytes, SerializerOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Lookups.Add(1, new KeyValuePair<string, object?>("result", "error"));
            logger.LogWarning(ex, "Product cache read failed for {ProductId}; falling back to the database", id);
            return null;
        }
    }

    private async Task TryWriteAsync(ProductDto product, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetAsync(
                Key(product.Id),
                JsonSerializer.SerializeToUtf8Bytes(product, SerializerOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = options.Value.TimeToLive },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Product cache write failed for {ProductId}", product.Id);
        }
    }

    private static string Key(Guid id) => $"product:{id:N}";
}
