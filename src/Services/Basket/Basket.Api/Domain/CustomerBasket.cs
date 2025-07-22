namespace Basket.Api.Domain;

public sealed record BasketItem(Guid ProductId, int Quantity);

/// <summary>
/// A customer's basket: products and quantities only. Prices are not kept here; the catalog
/// prices the items at checkout, so a basket never shows or charges a stale price.
/// </summary>
public sealed class CustomerBasket
{
    public const int MaxItems = 20;
    public const int MaxQuantity = 100;

    public CustomerBasket(string customerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        CustomerId = customerId;
    }

    public string CustomerId { get; }

    public List<BasketItem> Items { get; init; } = [];

    /// <summary>
    /// Order id reserved by a checkout that has not finished yet. Retrying the checkout
    /// reuses it, so ordering sees the same id and creates at most one order.
    /// </summary>
    public Guid? PendingCheckoutId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Sets the quantity of a product; zero removes it.</summary>
    public void SetQuantity(Guid productId, int quantity, DateTimeOffset now)
    {
        if (productId == Guid.Empty)
        {
            throw new BasketDomainException("A product id is required.");
        }

        if (quantity is < 0 or > MaxQuantity)
        {
            throw new BasketDomainException($"Quantity must be between 0 and {MaxQuantity}.");
        }

        var index = Items.FindIndex(item => item.ProductId == productId);
        if (quantity == 0)
        {
            if (index >= 0)
            {
                Items.RemoveAt(index);
            }
        }
        else if (index >= 0)
        {
            Items[index] = Items[index] with { Quantity = quantity };
        }
        else
        {
            if (Items.Count == MaxItems)
            {
                throw new BasketDomainException($"A basket can hold at most {MaxItems} different products.");
            }

            Items.Add(new BasketItem(productId, quantity));
        }

        Changed(now);
    }

    public void Clear(DateTimeOffset now)
    {
        Items.Clear();
        Changed(now);
    }

    /// <summary>Returns the order id for this checkout, reserving one on the first attempt.</summary>
    public Guid BeginCheckout()
    {
        if (IsEmpty)
        {
            throw new BasketDomainException("The basket is empty.");
        }

        PendingCheckoutId ??= Guid.NewGuid();
        return PendingCheckoutId.Value;
    }

    private void Changed(DateTimeOffset now)
    {
        // A changed basket is a different order: a later checkout gets a new id instead of
        // being treated as a retry of the earlier one.
        PendingCheckoutId = null;
        UpdatedAt = now;
    }
}

public sealed class BasketDomainException(string message) : Exception(message);
