namespace Inventory.Api.Domain;

/// <summary>
/// Stock of one product. <see cref="Available"/> can be promised to new orders;
/// <see cref="Reserved"/> is held for orders whose payment is still in progress.
/// </summary>
public sealed class StockItem
{
    private StockItem()
    {
    }

    public Guid ProductId { get; private set; }
    public int Available { get; private set; }
    public int Reserved { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static StockItem Create(Guid productId, int available, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(available);
        return new StockItem { ProductId = productId, Available = available, UpdatedAt = now };
    }

    public void SetAvailable(int available, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(available);
        Available = available;
        UpdatedAt = now;
    }

    internal void Reserve(int quantity, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (quantity > Available)
        {
            throw new InvalidOperationException($"Only {Available} of product {ProductId} available.");
        }

        Available -= quantity;
        Reserved += quantity;
        UpdatedAt = now;
    }

    internal void Release(int quantity, DateTimeOffset now)
    {
        TakeFromReserved(quantity);
        Available += quantity;
        UpdatedAt = now;
    }

    internal void Commit(int quantity, DateTimeOffset now)
    {
        TakeFromReserved(quantity);
        UpdatedAt = now;
    }

    private void TakeFromReserved(int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (quantity > Reserved)
        {
            throw new InvalidOperationException($"Only {Reserved} of product {ProductId} reserved.");
        }

        Reserved -= quantity;
    }
}
