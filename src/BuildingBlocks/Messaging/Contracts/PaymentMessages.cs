namespace BuildingBlocks.Messaging.Contracts;

public sealed record PaymentSucceeded(Guid OrderId, Guid PaymentId) : IIntegrationMessage;

public sealed record PaymentFailed(Guid OrderId, string Reason) : IIntegrationMessage;
