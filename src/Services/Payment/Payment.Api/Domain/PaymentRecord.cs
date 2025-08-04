namespace Payment.Api.Domain;

public enum PaymentStatus
{
    Succeeded,
    Declined,

    /// <summary>A succeeded charge that was given back.</summary>
    Refunded,

    /// <summary>
    /// A refund arrived before any charge. The record blocks a late charge command for the
    /// same order, which is answered with a failure instead of taking money.
    /// </summary>
    Voided,
}

/// <summary>The result of charging one order. There is at most one per order.</summary>
public sealed class PaymentRecord
{
    private PaymentRecord()
    {
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string CustomerId { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }
    public string? DeclineReason { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }
    public string? RefundReason { get; private set; }

    /// <summary>The reason a charge command for this order is answered with a failure.</summary>
    public string FailureReason => Status switch
    {
        PaymentStatus.Refunded => "The payment was refunded.",
        _ => DeclineReason ?? "Payment declined.",
    };

    public static PaymentRecord From(
        Guid orderId, string customerId, decimal amount, string currency, PaymentDecision decision, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Currency = currency,
            Status = decision.Approved ? PaymentStatus.Succeeded : PaymentStatus.Declined,
            DeclineReason = decision.DeclineReason,
            ProcessedAt = now,
        };

    public static PaymentRecord Void(
        Guid orderId, string customerId, decimal amount, string currency, string reason, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Currency = currency,
            Status = PaymentStatus.Voided,
            DeclineReason = "The payment was voided before it was taken.",
            RefundReason = reason,
            ProcessedAt = now,
            RefundedAt = now,
        };

    /// <returns><c>true</c> if money was given back; <c>false</c> if there was nothing to refund.</returns>
    public bool Refund(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status != PaymentStatus.Succeeded)
        {
            return false;
        }

        Status = PaymentStatus.Refunded;
        RefundReason = reason;
        RefundedAt = now;
        return true;
    }
}
