namespace BuildingBlocks.Outbox;

/// <summary>
/// A message written in the same transaction as the business change that produced it and
/// published to the broker afterwards by <see cref="OutboxDispatcher{TDbContext}"/>.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }

    /// <summary>W3C traceparent of the operation that enqueued the message.</summary>
    public string? TraceParent { get; set; }

    public DateTime OccurredOnUtc { get; set; }
    public DateTime? ProcessedOnUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
