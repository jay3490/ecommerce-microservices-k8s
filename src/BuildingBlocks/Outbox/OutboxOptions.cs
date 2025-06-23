namespace BuildingBlocks.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>How often the dispatcher looks for messages when it has not been notified.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; set; } = 50;

    /// <summary>After this many failed publish attempts a message is left for an operator.</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>Processed outbox and inbox rows older than this are deleted.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
