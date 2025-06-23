namespace BuildingBlocks.Inbox;

/// <summary>
/// Records that a consumer has processed a message. The (MessageId, Consumer) primary key is
/// what makes redelivered or duplicated messages detectable.
/// </summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public string Consumer { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public DateTime ProcessedOnUtc { get; set; }
}
