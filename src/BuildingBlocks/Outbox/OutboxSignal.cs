using System.Threading.Channels;

namespace BuildingBlocks.Outbox;

/// <summary>
/// Lets a committed transaction wake the dispatcher early. Notifications coalesce: many
/// commits while the dispatcher is busy result in one extra dispatch pass.
/// </summary>
public sealed class OutboxSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _channel.Writer.TryWrite(true);

    /// <summary>Completes when notified or after <paramref name="timeout"/>, whichever is first.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await _channel.Reader.ReadAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Polling interval elapsed without a notification.
        }
    }
}
