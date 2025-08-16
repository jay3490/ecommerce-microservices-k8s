using BuildingBlocks.Messaging.RabbitMq;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace BuildingBlocks.Messaging.DeadLetters;

/// <summary>
/// The service's <c>{queue}.dead-letter</c> queue. Each session uses its own channel with
/// publisher confirms; messages are fetched with <c>basic.get</c> without auto-ack, so closing
/// the channel returns every message that was not acknowledged to the queue.
/// </summary>
public sealed class RabbitMqDeadLetterQueue(RabbitMqConnectionProvider connections, IOptions<MessagingOptions> options)
    : IDeadLetterQueue
{
    private readonly MessagingOptions _options = options.Value;

    public string Name => _options.DeadLetterQueue;

    public async Task<IDeadLetterSession> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = await connections.GetConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        return new Session(channel, _options);
    }

    private sealed class Session(IChannel channel, MessagingOptions options) : IDeadLetterSession
    {
        private static readonly string[] DeathHeaders =
            ["x-death", "x-first-death-exchange", "x-first-death-queue", "x-first-death-reason",
             "x-last-death-exchange", "x-last-death-queue", "x-last-death-reason"];

        public async Task<RawDeadLetter?> NextAsync(CancellationToken cancellationToken)
        {
            var result = await channel.BasicGetAsync(options.DeadLetterQueue, autoAck: false, cancellationToken);
            if (result is null)
            {
                return null;
            }

            var properties = result.BasicProperties;
            return new RawDeadLetter(
                result.DeliveryTag,
                properties.MessageId,
                properties.Type,
                properties.CorrelationId,
                Normalize(properties.Headers),
                result.Body.ToArray());
        }

        public async Task RepublishAsync(RawDeadLetter message, CancellationToken cancellationToken)
        {
            var headers = message.Headers
                .Where(header => !DeathHeaders.Contains(header.Key))
                .ToDictionary(header => header.Key, header => header.Value);
            headers[DeadLetterHeaders.ReplayCount] =
                (message.Headers.GetValueOrDefault(DeadLetterHeaders.ReplayCount) is int replays ? replays : 0) + 1;

            var properties = new BasicProperties
            {
                MessageId = message.MessageId,
                Type = message.MessageType,
                CorrelationId = message.CorrelationId,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Headers = headers,
            };

            // The default exchange routes by queue name: only this service gets the message again.
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: options.QueueName,
                mandatory: true,
                basicProperties: properties,
                body: message.Body,
                cancellationToken: cancellationToken);
        }

        public Task AcknowledgeAsync(RawDeadLetter message, CancellationToken cancellationToken) =>
            channel.BasicAckAsync(message.DeliveryTag, multiple: false, cancellationToken).AsTask();

        public async ValueTask DisposeAsync()
        {
            await channel.CloseAsync();
            await channel.DisposeAsync();
        }

        // Turns the AMQP timestamps in x-death entries into DateTimeOffset so the transport-neutral
        // service can read them.
        private static Dictionary<string, object?> Normalize(IDictionary<string, object?>? headers)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (headers is null)
            {
                return result;
            }

            foreach (var (key, value) in headers)
            {
                result[key] = key == "x-death" && value is IEnumerable<object?> deaths
                    ? deaths.OfType<IDictionary<string, object?>>()
                        .Select(death => (object?)death.ToDictionary(
                            entry => entry.Key,
                            entry => entry.Value is AmqpTimestamp at
                                ? DateTimeOffset.FromUnixTimeSeconds(at.UnixTime)
                                : entry.Value))
                        .ToList()
                    : value;
            }

            return result;
        }
    }
}
