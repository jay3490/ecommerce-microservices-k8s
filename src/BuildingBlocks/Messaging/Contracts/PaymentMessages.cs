namespace BuildingBlocks.Messaging.Contracts;

public sealed record PaymentSucceeded(Guid OrderId, Guid PaymentId) : IIntegrationMessage;

public sealed record PaymentFailed(Guid OrderId, string Reason) : IIntegrationMessage;

/// <summary>
/// Compensating command from the order saga: give the money back. Sent when a later step
/// failed, or when the payment step timed out and it is unknown whether a charge was made;
/// a refund that arrives before the charge voids the order's payment instead.
/// </summary>
public sealed record RefundPayment(Guid OrderId, string CustomerId, decimal Amount, string Currency, string Reason) : IIntegrationMessage;

/// <summary>The order holds no money any more: refunded, voided, or never charged.</summary>
public sealed record PaymentRefunded(Guid OrderId, Guid? PaymentId) : IIntegrationMessage;
