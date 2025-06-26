namespace Ordering.Api.Domain;

public enum OrderStatus
{
    Pending,
    Confirmed,
    Cancelled,
}

/// <summary>A priced line as it is being added to a new order.</summary>
public sealed record OrderItem(Guid ProductId, string ProductName, decimal UnitPrice, string Currency, int Quantity);

/// <summary>
/// Order aggregate. All changes go through its methods so the invariants hold at all times:
/// an order has 1..<see cref="MaxLines"/> lines in a single currency, each with a positive
/// price and a quantity of 1..<see cref="MaxQuantityPerLine"/>; it starts Pending and moves
/// once, to Confirmed or Cancelled.
/// </summary>
public sealed class Order
{
    public const int MaxLines = 20;
    public const int MaxQuantityPerLine = 100;
    public const int MaxCustomerIdLength = 100;

    private readonly List<OrderLine> _lines = [];

    private Order()
    {
    }

    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public decimal Total { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    public static Order Place(Guid id, string customerId, string currency, IEnumerable<OrderItem> items, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(customerId) || customerId.Length > MaxCustomerIdLength)
        {
            throw new OrderingDomainException("An order needs a customer id of at most 100 characters.");
        }

        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetter))
        {
            throw new OrderingDomainException($"'{currency}' is not an ISO 4217 currency code.");
        }

        var order = new Order
        {
            Id = id,
            CustomerId = customerId,
            Currency = currency.ToUpperInvariant(),
            Status = OrderStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };

        foreach (var item in items)
        {
            order.AddLine(item);
        }

        if (order._lines.Count == 0)
        {
            throw new OrderingDomainException("An order needs at least one line.");
        }

        order.Total = order._lines.Sum(line => line.LineTotal);
        return order;
    }

    public void Confirm(DateTimeOffset now)
    {
        if (Status == OrderStatus.Confirmed)
        {
            return;
        }

        if (Status != OrderStatus.Pending)
        {
            throw new OrderingDomainException($"Order {Id} is {Status} and can no longer be confirmed.");
        }

        Status = OrderStatus.Confirmed;
        UpdatedAt = now;
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status != OrderStatus.Pending)
        {
            throw new OrderingDomainException($"Order {Id} is {Status} and can no longer be cancelled.");
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        UpdatedAt = now;
    }

    private void AddLine(OrderItem item)
    {
        if (item.Quantity is < 1 or > MaxQuantityPerLine)
        {
            throw new OrderingDomainException($"Quantity must be between 1 and {MaxQuantityPerLine}.");
        }

        if (item.UnitPrice <= 0)
        {
            throw new OrderingDomainException($"Product {item.ProductId} has no valid price.");
        }

        if (!string.Equals(item.Currency, Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new OrderingDomainException(
                $"Product {item.ProductId} is priced in {item.Currency} but the order is in {Currency}.");
        }

        var existing = _lines.Find(line => line.ProductId == item.ProductId);
        if (existing is null)
        {
            if (_lines.Count == MaxLines)
            {
                throw new OrderingDomainException($"An order can have at most {MaxLines} lines.");
            }

            _lines.Add(new OrderLine(item.ProductId, item.ProductName, item.UnitPrice, item.Quantity));
            return;
        }

        // The same product twice becomes one line, as long as both mentions agree on the price.
        if (existing.UnitPrice != item.UnitPrice)
        {
            throw new OrderingDomainException($"Product {item.ProductId} appears with two different prices.");
        }

        if (existing.Quantity + item.Quantity > MaxQuantityPerLine)
        {
            throw new OrderingDomainException($"Quantity must be between 1 and {MaxQuantityPerLine}.");
        }

        existing.AddQuantity(item.Quantity);
    }
}

public sealed class OrderLine
{
    internal OrderLine(Guid productId, string productName, decimal unitPrice, int quantity)
    {
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; }
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal LineTotal => UnitPrice * Quantity;

    internal void AddQuantity(int quantity) => Quantity += quantity;
}

public sealed class OrderingDomainException(string message) : Exception(message);
