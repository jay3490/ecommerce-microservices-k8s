namespace BuildingBlocks.Observability;

/// <summary>
/// Holds the correlation id of the operation currently executing on this async flow.
/// Set by <see cref="CorrelationIdMiddleware"/> for HTTP requests and by the message
/// consumer for incoming messages, and read wherever an id has to be passed on.
/// </summary>
public static class CorrelationContext
{
    public const string HeaderName = "X-Correlation-ID";
    public const string LogPropertyName = "CorrelationId";

    private const int MaxLength = 128;
    private static readonly AsyncLocal<string?> CurrentId = new();

    public static string? Current
    {
        get => CurrentId.Value;
        set => CurrentId.Value = value;
    }

    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Accepts a caller-supplied id only when it is short and made of safe characters,
    /// so a client cannot inject arbitrary text into logs and downstream headers.
    /// </summary>
    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaxLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
