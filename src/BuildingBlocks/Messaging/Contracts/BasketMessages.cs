namespace BuildingBlocks.Messaging.Contracts;

/// <summary>A basket line priced by the catalog at checkout time.</summary>
public sealed record CheckoutLine(Guid ProductId, string ProductName, decimal UnitPrice, string Currency, int Quantity);

/// <summary>
/// A customer checked out their basket. Ordering turns it into an order with id
/// <paramref name="OrderId"/>; the basket service picks that id, so a retried checkout
/// produces the same order rather than a second one.
/// </summary>
public sealed record BasketCheckedOut(Guid OrderId, string CustomerId, IReadOnlyList<CheckoutLine> Lines) : IIntegrationMessage;
