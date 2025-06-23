using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BuildingBlocks.Messaging.RabbitMq;

/// <summary>
/// Consumes the service's queue with manual acknowledgements. Processed and duplicate
/// messages are acked; rejected or exhausted ones are nacked without requeue, which routes
/// them to the service's dead-letter queue.
/// </summary>
public sealed class RabbitMqConsumerService(
    RabbitMqConnectionProvider connections,
    MessageHandlerRegistry registry,
    MessageProcessor processor,
    IOptions<MessagingOptions> options,
    ILogger<RabbitMqConsumerService> logger) : BackgroundService
{
    private readonly MessagingOptions _options = options.Value;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (registry.MessageTypes.Count == 0)
        {
            return;
        }

        try
        {
            var connection = await connections.GetConnectionAsync(stoppingToken);
            var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
            _channel = channel;

            await RabbitMqTopology.DeclareConsumerQueueAsync(channel, _options, registry.MessageTypes, stoppingToken);
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: _options.PrefetchCount, global: false, stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => HandleDeliveryAsync(channel, delivery, stoppingToken);
            await channel.BasicConsumeAsync(_options.QueueName, autoAck: false, consumer, stoppingToken);

            logger.LogInformation("Consuming {Queue} for {MessageTypes}", _options.QueueName, string.Join(", ", registry.MessageTypes));
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var message = new IncomingMessage(
            Guid.TryParse(delivery.BasicProperties.MessageId, out var id) ? id : Guid.Empty,
            delivery.BasicProperties.Type ?? delivery.RoutingKey,
            delivery.Body,
            delivery.BasicProperties.CorrelationId,
            ReadHeader(delivery.BasicProperties, "traceparent"));

        ProcessingOutcome outcome;
        try
        {
            outcome = await processor.ProcessAsync(message, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: leave the delivery unacknowledged so the broker hands it out again.
            return;
        }

        if (outcome is ProcessingOutcome.Processed or ProcessingOutcome.Duplicate)
        {
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
        }
        else
        {
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
        }
    }

    private static string? ReadHeader(IReadOnlyBasicProperties properties, string name) =>
        properties.Headers?.TryGetValue(name, out var value) == true
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => null,
            }
            : null;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
            await _channel.DisposeAsync();
        }
    }
}
