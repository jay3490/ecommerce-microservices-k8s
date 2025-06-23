namespace BuildingBlocks.Messaging;

public sealed class MessagingOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>Topic exchange every service publishes to; routing key = message type.</summary>
    public string Exchange { get; set; } = "ecommerce";

    /// <summary>Queue this service consumes from. Set in code by <c>AddMessaging</c>.</summary>
    public string QueueName { get; set; } = string.Empty;

    public ushort PrefetchCount { get; set; } = 16;

    /// <summary>Retries of a failing handler before the message is dead-lettered.</summary>
    public int RetryCount { get; set; } = 3;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    public string DeadLetterExchange => $"{Exchange}.dead-letter";

    public string DeadLetterQueue => $"{QueueName}.dead-letter";
}
