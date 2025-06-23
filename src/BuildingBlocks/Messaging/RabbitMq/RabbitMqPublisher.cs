using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using BuildingBlocks.Observability;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace BuildingBlocks.Messaging.RabbitMq;

/// <summary>
/// Publishes with publisher confirms and <c>mandatory</c> set, so a message that the broker
/// could not route to any queue (for example because the consuming service has not declared
/// its queue yet) fails the publish and stays in the outbox for another attempt.
/// </summary>
public sealed class RabbitMqPublisher(RabbitMqConnectionProvider connections, IOptions<MessagingOptions> options)
    : IMessagePublisher, IAsyncDisposable
{
    private static readonly Counter<long> Published =
        Telemetry.Meter.CreateCounter<long>("messaging.messages.published", description: "Messages confirmed by the broker");

    private readonly MessagingOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        ActivityContext.TryParse(message.TraceParent, null, out var parent);
        using var activity = Telemetry.ActivitySource.StartActivity($"{message.MessageType} publish", ActivityKind.Producer, parent);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation", "publish");
        activity?.SetTag("messaging.destination.name", _options.Exchange);
        activity?.SetTag("messaging.message.id", message.MessageId);

        var properties = new BasicProperties
        {
            MessageId = message.MessageId.ToString(),
            Type = message.MessageType,
            CorrelationId = message.CorrelationId,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>(),
        };

        // Continue the trace in the consumer: either this publish span or the span that
        // originally enqueued the message.
        var traceParent = activity?.Id ?? message.TraceParent;
        if (traceParent is not null)
        {
            properties.Headers["traceparent"] = traceParent;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            await channel.BasicPublishAsync(
                _options.Exchange,
                routingKey: message.MessageType,
                mandatory: true,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(message.Payload),
                cancellationToken: cancellationToken);
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            _gate.Release();
        }

        Published.Add(1, new KeyValuePair<string, object?>("message.type", message.MessageType));
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        var connection = await connections.GetConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await RabbitMqTopology.DeclareExchangeAsync(channel, _options, cancellationToken);

        _channel = channel;
        return channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _gate.Dispose();
    }
}
