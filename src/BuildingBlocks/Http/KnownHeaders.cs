namespace BuildingBlocks.Http;

public static class KnownHeaders
{
    /// <summary>
    /// Authenticated customer id. Set by the gateway from the validated JWT <c>sub</c> claim
    /// after stripping any value sent by the client; services behind the gateway trust it.
    /// </summary>
    public const string CustomerId = "X-Customer-Id";
}
