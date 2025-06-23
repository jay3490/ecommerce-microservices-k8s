using RabbitMQ.Client;

namespace BuildingBlocks.Messaging.RabbitMq;

/// <summary>
/// Declares the broker objects. All declarations are idempotent, so every service declares
/// what it needs on startup instead of relying on a separate provisioning step.
/// </summary>
/// <remarks>
/// <code>
/// ecommerce (topic) --[message type]--> {service} --(nack)--> ecommerce.dead-letter (direct) --> {service}.dead-letter
/// </code>
/// </remarks>
internal static class RabbitMqTopology
{
    public static Task DeclareExchangeAsync(IChannel channel, MessagingOptions options, CancellationToken cancellationToken) =>
        channel.ExchangeDeclareAsync(
            options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);

    public static async Task DeclareConsumerQueueAsync(
        IChannel channel, MessagingOptions options, IEnumerable<string> messageTypes, CancellationToken cancellationToken)
    {
        await DeclareExchangeAsync(channel, options, cancellationToken);

        await channel.ExchangeDeclareAsync(
            options.DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            options.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            options.DeadLetterQueue, options.DeadLetterExchange, routingKey: options.QueueName, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = options.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = options.QueueName,
            },
            cancellationToken: cancellationToken);

        foreach (var messageType in messageTypes)
        {
            await channel.QueueBindAsync(
                options.QueueName, options.Exchange, routingKey: messageType, cancellationToken: cancellationToken);
        }
    }
}
