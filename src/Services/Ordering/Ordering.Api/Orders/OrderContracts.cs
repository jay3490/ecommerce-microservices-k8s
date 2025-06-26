using Ordering.Api.Domain;

namespace Ordering.Api.Orders;

public sealed record PlaceOrderRequest(IReadOnlyList<PlaceOrderItem>? Items)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (Items is null || Items.Count is 0 or > Order.MaxLines)
        {
            errors[nameof(Items)] = [$"An order needs between 1 and {Order.MaxLines} items."];
            return errors;
        }

        if (Items.Any(item => item.ProductId == Guid.Empty))
        {
            errors["Items.ProductId"] = ["Every item needs a product id."];
        }

        if (Items.Any(item => item.Quantity is < 1 or > Order.MaxQuantityPerLine))
        {
            errors["Items.Quantity"] = [$"Quantities must be between 1 and {Order.MaxQuantityPerLine}."];
        }

        return errors;
    }
}

public sealed record PlaceOrderItem(Guid ProductId, int Quantity);

public sealed record OrderLineDto(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

public sealed record OrderDto(
    Guid Id,
    string Status,
    decimal Total,
    string Currency,
    string? CancellationReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OrderLineDto> Lines)
{
    public static OrderDto From(Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.Total,
        order.Currency,
        order.CancellationReason,
        order.CreatedAt,
        order.UpdatedAt,
        order.Lines.Select(l => new OrderLineDto(l.ProductId, l.ProductName, l.UnitPrice, l.Quantity, l.LineTotal)).ToList());
}
