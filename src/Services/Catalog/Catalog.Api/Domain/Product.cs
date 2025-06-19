namespace Catalog.Api.Domain;

public sealed class Product
{
    public const int MaxNameLength = 200;
    public const int MaxSkuLength = 64;
    public const int MaxDescriptionLength = 2000;

    private Product()
    {
    }

    public Guid Id { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Product Create(
        Guid id, string sku, string name, string description, decimal price, string currency, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        var product = new Product
        {
            Id = id,
            Sku = sku.Trim().ToUpperInvariant(),
            Currency = currency.ToUpperInvariant(),
            CreatedAt = now,
        };
        product.Update(name, description, price, now);
        return product;
    }

    public void Update(string name, string description, decimal price, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price);

        Name = name.Trim();
        Description = description.Trim();
        Price = price;
        UpdatedAt = now;
    }
}
