using System.Text;

namespace BuildingBlocks.Messaging.DeadLetters;

/// <summary>A message as fetched from a dead-letter queue, still held by the session.</summary>
public sealed record RawDeadLetter(
    ulong DeliveryTag,
    string? MessageId,
    string? MessageType,
    string? CorrelationId,
    IReadOnlyDictionary<string, object?> Headers,
    ReadOnlyMemory<byte> Body);

/// <summary>
/// A short-lived hold on a dead-letter queue. Fetched messages stay invisible to everyone else
/// until they are acknowledged (gone for good) or the session ends, which puts every message
/// that was not acknowledged back on the queue.
/// </summary>
public interface IDeadLetterSession : IAsyncDisposable
{
    /// <returns>The next message, or null when the queue has no more.</returns>
    Task<RawDeadLetter?> NextAsync(CancellationToken cancellationToken);

    /// <summary>Sends a copy back to the service's own queue and waits for the broker's confirm.</summary>
    Task RepublishAsync(RawDeadLetter message, CancellationToken cancellationToken);

    /// <summary>Removes the message from the dead-letter queue.</summary>
    Task AcknowledgeAsync(RawDeadLetter message, CancellationToken cancellationToken);
}

public interface IDeadLetterQueue
{
    /// <summary>Name of the dead-letter queue, for display.</summary>
    string Name { get; }

    Task<IDeadLetterSession> OpenAsync(CancellationToken cancellationToken);
}

public sealed record DeadLetterDto(
    string? MessageId,
    string? MessageType,
    string? CorrelationId,
    string? Reason,
    DateTimeOffset? DeadLetteredAt,
    long DeathCount,
    int ReplayCount,
    string BodyPreview);

public sealed record DeadLetterResult(int Matched, int Processed, int Failed);

/// <summary>
/// Lists, replays and discards dead-lettered messages. Replaying sends the message back to the
/// owning service's queue only (through the default exchange), not to the topic exchange, so
/// other services that already handled it do not see it again.
/// </summary>
public sealed class DeadLetterService(IDeadLetterQueue queue)
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 1000;
    private const int PreviewLength = 500;

    public string QueueName => queue.Name;

    /// <summary>Looks at up to <paramref name="limit"/> messages without removing any.</summary>
    public async Task<IReadOnlyList<DeadLetterDto>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var result = new List<DeadLetterDto>();
        await using var session = await queue.OpenAsync(cancellationToken);
        while (result.Count < Clamp(limit) && await session.NextAsync(cancellationToken) is { } message)
        {
            result.Add(Describe(message));
        }

        return result;
    }

    /// <summary>
    /// Replays the messages whose id is in <paramref name="messageIds"/>, or every message when
    /// it is null, looking at no more than <paramref name="limit"/> messages. A message whose
    /// republish fails stays in the dead-letter queue.
    /// </summary>
    public Task<DeadLetterResult> ReplayAsync(IReadOnlySet<string>? messageIds, int limit, CancellationToken cancellationToken) =>
        ProcessAsync(messageIds, limit, async (session, message, token) =>
        {
            await session.RepublishAsync(message, token);
            await session.AcknowledgeAsync(message, token);
        }, cancellationToken);

    /// <summary>Deletes the given messages from the dead-letter queue for good.</summary>
    public Task<DeadLetterResult> DiscardAsync(IReadOnlySet<string> messageIds, int limit, CancellationToken cancellationToken) =>
        ProcessAsync(messageIds, limit, (session, message, token) => session.AcknowledgeAsync(message, token), cancellationToken);

    public static DeadLetterDto Describe(RawDeadLetter message)
    {
        var death = FirstDeath(message.Headers);
        var body = Encoding.UTF8.GetString(message.Body.Span);
        DateTimeOffset? diedAt = Entry(death, "time") switch
        {
            DateTimeOffset at => at,
            DateTime at => new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)),
            long seconds => DateTimeOffset.FromUnixTimeSeconds(seconds),
            _ => null,
        };

        return new DeadLetterDto(
            message.MessageId,
            message.MessageType,
            message.CorrelationId,
            Text(Entry(death, "reason")),
            diedAt,
            Entry(death, "count") is long count ? count : 0,
            message.Headers.GetValueOrDefault(DeadLetterHeaders.ReplayCount) is int replays ? replays : 0,
            body.Length > PreviewLength ? body[..PreviewLength] + "..." : body);
    }

    private async Task<DeadLetterResult> ProcessAsync(
        IReadOnlySet<string>? messageIds,
        int limit,
        Func<IDeadLetterSession, RawDeadLetter, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        int seen = 0, matched = 0, processed = 0, failed = 0;
        await using var session = await queue.OpenAsync(cancellationToken);
        while (seen < Clamp(limit) && await session.NextAsync(cancellationToken) is { } message)
        {
            seen++;
            if (messageIds is not null && (message.MessageId is null || !messageIds.Contains(message.MessageId)))
            {
                continue;
            }

            matched++;
            try
            {
                await action(session, message, cancellationToken);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
            }
        }

        return new DeadLetterResult(matched, processed, failed);
    }

    private static int Clamp(int limit) => Math.Clamp(limit, 1, MaxLimit);

    private static IDictionary<string, object?>? FirstDeath(IReadOnlyDictionary<string, object?> headers) =>
        headers.GetValueOrDefault("x-death") is IEnumerable<object?> deaths
            ? deaths.OfType<IDictionary<string, object?>>().FirstOrDefault()
            : null;

    private static object? Entry(IDictionary<string, object?>? death, string key) =>
        death is not null && death.TryGetValue(key, out var value) ? value : null;

    private static string? Text(object? value) => value switch
    {
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        string text => text,
        _ => null,
    };
}

public static class DeadLetterHeaders
{
    /// <summary>How many times an operator has replayed the message.</summary>
    public const string ReplayCount = "x-replay-count";
}
