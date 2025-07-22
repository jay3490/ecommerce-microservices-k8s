using Basket.Api.Catalog;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;

namespace Basket.Api.Baskets;

public enum CheckoutStatus
{
    Accepted,
    EmptyBasket,
    UnknownProducts,
    CatalogUnavailable,
    BrokerUnavailable,
}

public sealed record CheckoutResult(CheckoutStatus Status, Guid? OrderId = null, IReadOnlyList<Guid>? UnknownProducts = null);

/// <summary>
/// Turns a basket into a <see cref="BasketCheckedOut"/> message:
/// reserve an order id, price the items with the catalog, publish with a broker
/// confirmation, and only then delete the basket. If any step fails the basket stays as it
/// is (with the reserved id), so the customer can simply try again.
/// </summary>
public sealed class BasketCheckout(
    IBasketStore baskets,
    IProductPricing pricing,
    IMessagePublisher publisher,
    ILogger<BasketCheckout> logger)
{
    public async Task<CheckoutResult> CheckoutAsync(string customerId, CancellationToken cancellationToken)
    {
        var basket = await baskets.GetAsync(customerId, cancellationToken);
        if (basket is null || basket.IsEmpty)
        {
            return new CheckoutResult(CheckoutStatus.EmptyBasket);
        }

        var hadPendingCheckout = basket.PendingCheckoutId is not null;
        var orderId = basket.BeginCheckout();
        if (!hadPendingCheckout)
        {
            await baskets.SaveAsync(basket, cancellationToken);
        }

        IReadOnlyList<PricedProduct> products;
        try
        {
            products = await pricing.GetProductsAsync(basket.Items.Select(i => i.ProductId).ToList(), cancellationToken);
        }
        catch (Exception ex) when (CatalogPricingClient.IsUnavailable(ex))
        {
            logger.LogWarning(ex, "Catalog unavailable during checkout {OrderId}", orderId);
            return new CheckoutResult(CheckoutStatus.CatalogUnavailable, orderId);
        }

        var byId = products.ToDictionary(p => p.Id);
        var unknown = basket.Items.Select(i => i.ProductId).Where(id => !byId.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            return new CheckoutResult(CheckoutStatus.UnknownProducts, orderId, unknown);
        }

        var lines = basket.Items
            .Select(item =>
            {
                var product = byId[item.ProductId];
                return new CheckoutLine(product.Id, product.Name, product.Price, product.Currency, item.Quantity);
            })
            .ToList();

        try
        {
            await publisher.PublishAsync(new BasketCheckedOut(orderId, customerId, lines), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not publish checkout {OrderId}; the basket is kept for a retry", orderId);
            return new CheckoutResult(CheckoutStatus.BrokerUnavailable, orderId);
        }

        // The order is on its way at this point. If the delete fails the basket is still there,
        // and checking it out again sends the same order id, which ordering ignores.
        try
        {
            await baskets.DeleteAsync(customerId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Checkout {OrderId} was published but the basket could not be deleted", orderId);
        }

        logger.LogInformation("Basket of {CustomerId} checked out as order {OrderId}", customerId, orderId);
        return new CheckoutResult(CheckoutStatus.Accepted, orderId);
    }
}
