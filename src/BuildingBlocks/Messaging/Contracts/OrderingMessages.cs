namespace BuildingBlocks.Messaging.Contracts;

public sealed record OrderLineItem(Guid ProductId, int Quantity);

/// <summary>An order was accepted by the API and is waiting for the saga to process it.</summary>
public sealed record OrderSubmitted(
    Guid OrderId,
    string CustomerId,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderLineItem> Lines) : IIntegrationMessage;

/// <summary>Command from the order saga to the inventory service.</summary>
public sealed record ReserveStock(Guid OrderId, IReadOnlyList<OrderLineItem> Lines) : IIntegrationMessage;

/// <summary>Compensating command: give back the stock reserved for an order.</summary>
public sealed record ReleaseStock(Guid OrderId, string Reason) : IIntegrationMessage;

/// <summary>Command from the order saga to the payment service.</summary>
public sealed record ProcessPayment(Guid OrderId, string CustomerId, decimal Amount, string Currency) : IIntegrationMessage;

/// <summary>
/// Paid and booked for shipping: the order is final. Inventory commits the reservation and
/// the customer is notified.
/// </summary>
public sealed record OrderConfirmed(Guid OrderId, string CustomerId, decimal Total, string Currency) : IIntegrationMessage;

/// <summary>The saga gave up on the order and has undone every step that had been taken.</summary>
public sealed record OrderCancelled(Guid OrderId, string CustomerId, string Reason) : IIntegrationMessage;
