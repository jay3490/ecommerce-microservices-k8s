using System.Collections.Concurrent;
using Basket.Api.Baskets;
using Basket.Api.Catalog;
using Basket.Api.Domain;
using BuildingBlocks.Messaging;

namespace Basket.Tests;

/// <summary>Stores copies, like Redis would, so tests notice changes that were never saved.</summary>
public sealed class InMemoryBasketStore : IBasketStore
{
    private readonly ConcurrentDictionary<string, CustomerBasket> _baskets = new();

    public bool FailDeletes { get; set; }

    public Task<CustomerBasket?> GetAsync(string customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_baskets.TryGetValue(customerId, out var basket) ? Copy(basket) : null);

    public Task SaveAsync(CustomerBasket basket, CancellationToken cancellationToken)
    {
        _baskets[basket.CustomerId] = Copy(basket);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string customerId, CancellationToken cancellationToken)
    {
        if (FailDeletes)
        {
            throw new InvalidOperationException("Redis unavailable");
        }

        _baskets.TryRemove(customerId, out _);
        return Task.CompletedTask;
    }

    private static CustomerBasket Copy(CustomerBasket basket) => new(basket.CustomerId)
    {
        Items = basket.Items.ToList(),
        PendingCheckoutId = basket.PendingCheckoutId,
        UpdatedAt = basket.UpdatedAt,
    };
}

public sealed class FakePricing : IProductPricing
{
    public Dictionary<Guid, PricedProduct> Products { get; } = [];

    public Exception? Failure { get; set; }

    public Task<IReadOnlyList<PricedProduct>> GetProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        IReadOnlyList<PricedProduct> found = ids.Where(Products.ContainsKey).Select(id => Products[id]).ToList();
        return Task.FromResult(found);
    }

    public PricedProduct Add(string name, decimal price)
    {
        var product = new PricedProduct(Guid.NewGuid(), name.ToUpperInvariant(), name, price, "USD");
        Products[product.Id] = product;
        return product;
    }
}

public sealed class FakePublisher : IMessagePublisher
{
    private readonly List<OutgoingMessage> _messages = [];

    public bool Fail { get; set; }

    public IReadOnlyList<OutgoingMessage> Messages
    {
        get
        {
            lock (_messages)
            {
                return _messages.ToList();
            }
        }
    }

    public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Broker unavailable");
        }

        lock (_messages)
        {
            _messages.Add(message);
        }

        return Task.CompletedTask;
    }
}
