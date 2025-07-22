using System.Text.Json;
using Basket.Api.Domain;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Basket.Api.Baskets;

public sealed class BasketOptions
{
    public const string SectionName = "Basket";

    /// <summary>A basket nobody touches for this long expires.</summary>
    public TimeSpan TimeToLive { get; set; } = TimeSpan.FromDays(30);
}

public interface IBasketStore
{
    Task<CustomerBasket?> GetAsync(string customerId, CancellationToken cancellationToken);

    Task SaveAsync(CustomerBasket basket, CancellationToken cancellationToken);

    Task DeleteAsync(string customerId, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps each basket as one JSON string under <c>basket:{customerId}</c> with a sliding
/// expiry. Concurrent writes from the same customer are last-writer-wins.
/// </summary>
public sealed class RedisBasketStore(IConnectionMultiplexer redis, IOptions<BasketOptions> options) : IBasketStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<CustomerBasket?> GetAsync(string customerId, CancellationToken cancellationToken)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(customerId));
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        var stored = JsonSerializer.Deserialize<StoredBasket>(value.ToString(), SerializerOptions)!;
        return new CustomerBasket(customerId)
        {
            Items = stored.Items.ToList(),
            PendingCheckoutId = stored.PendingCheckoutId,
            UpdatedAt = stored.UpdatedAt,
        };
    }

    public Task SaveAsync(CustomerBasket basket, CancellationToken cancellationToken)
    {
        var stored = new StoredBasket(basket.Items, basket.PendingCheckoutId, basket.UpdatedAt);
        return redis.GetDatabase().StringSetAsync(
            Key(basket.CustomerId),
            JsonSerializer.Serialize(stored, SerializerOptions),
            options.Value.TimeToLive);
    }

    public Task DeleteAsync(string customerId, CancellationToken cancellationToken) =>
        redis.GetDatabase().KeyDeleteAsync(Key(customerId));

    private static RedisKey Key(string customerId) => $"basket:{customerId}";

    private sealed record StoredBasket(IReadOnlyList<BasketItem> Items, Guid? PendingCheckoutId, DateTimeOffset UpdatedAt);
}
