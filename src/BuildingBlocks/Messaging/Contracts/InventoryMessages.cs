namespace BuildingBlocks.Messaging.Contracts;

public sealed record StockReserved(Guid OrderId, Guid ReservationId) : IIntegrationMessage;

public sealed record StockReservationFailed(Guid OrderId, string Reason) : IIntegrationMessage;

public sealed record StockReleased(Guid OrderId) : IIntegrationMessage;
