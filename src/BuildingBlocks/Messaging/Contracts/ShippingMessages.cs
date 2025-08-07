namespace BuildingBlocks.Messaging.Contracts;

/// <summary>Command from the order saga, after payment: book a shipment for the order.</summary>
public sealed record CreateShipment(Guid OrderId, string CustomerId, IReadOnlyList<OrderLineItem> Lines) : IIntegrationMessage;

public sealed record ShipmentCreated(Guid OrderId, Guid ShipmentId, string TrackingNumber) : IIntegrationMessage;

public sealed record ShipmentCreationFailed(Guid OrderId, string Reason) : IIntegrationMessage;

/// <summary>
/// Compensating command: the order is being cancelled. A shipment that has not left yet is
/// cancelled; if none exists, one is recorded as cancelled so a late CreateShipment fails.
/// </summary>
public sealed record CancelShipment(Guid OrderId, string Reason) : IIntegrationMessage;

/// <summary>The parcel left the warehouse.</summary>
public sealed record ShipmentDispatched(Guid OrderId, Guid ShipmentId, string CustomerId, string Carrier, string TrackingNumber)
    : IIntegrationMessage;

/// <summary>The carrier reported the parcel delivered.</summary>
public sealed record ShipmentDelivered(Guid OrderId, Guid ShipmentId, string CustomerId) : IIntegrationMessage;
