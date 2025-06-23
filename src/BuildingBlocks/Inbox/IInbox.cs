namespace BuildingBlocks.Inbox;

public interface IInbox
{
    /// <summary>
    /// Runs <paramref name="handler"/> unless the message was already processed by
    /// <paramref name="consumer"/>, and commits its changes together with the inbox record.
    /// </summary>
    /// <returns><c>true</c> if the handler ran; <c>false</c> for a duplicate.</returns>
    Task<bool> TryProcessAsync(
        Guid messageId,
        string messageType,
        string consumer,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken);
}
