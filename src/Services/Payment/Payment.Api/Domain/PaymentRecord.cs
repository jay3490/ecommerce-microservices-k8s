namespace Payment.Api.Domain;

public enum PaymentStatus
{
    Succeeded,
    Declined,
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
}
