using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using BuildingBlocks.Inbox;
using BuildingBlocks.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Serilog.Context;

namespace BuildingBlocks.Messaging;

public sealed record IncomingMessage(
    Guid MessageId,
    string MessageType,
    ReadOnlyMemory<byte> Body,
    string? CorrelationId,
    string? TraceParent);

public enum ProcessingOutcome
{
    /// <summary>The handler ran and its changes were committed.</summary>
    Processed,

    /// <summary>The message had already been processed by this consumer and was skipped.</summary>
    Duplicate,

    /// <summary>The message can never succeed (unknown type, bad id or body) and is dead-lettered.</summary>
    Rejected,

    /// <summary>The handler kept failing after all retries; the message is dead-lettered.</summary>
    Failed,
}

/// <summary>
/// Transport-independent processing of one delivery: resolves the handler, deserializes the
/// body and runs the handler through the inbox inside a retry pipeline. Each attempt gets a
/// fresh DI scope, so a failed attempt's tracked changes are thrown away.
/// </summary>
public sealed class MessageProcessor
{
    private static readonly Counter<long> Processed =
        Telemetry.Meter.CreateCounter<long>("messaging.messages.processed", description: "Consumed messages by outcome");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MessageHandlerRegistry _registry;
    private readonly ILogger<MessageProcessor> _logger;
    private readonly string _consumer;
    private readonly ResiliencePipeline _retry;

    public MessageProcessor(
        IServiceScopeFactory scopeFactory,
        MessageHandlerRegistry registry,
        IOptions<MessagingOptions> options,
        ILogger<MessageProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _logger = logger;
        _consumer = options.Value.QueueName;
        _retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = Math.Max(options.Value.RetryCount, 1),
                Delay = options.Value.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                // Retry anything except a shutdown; a timeout inside a handler is retried like any other error.
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is not null && !args.Context.CancellationToken.IsCancellationRequested),
                OnRetry = args =>
                {
                    _logger.LogWarning(args.Outcome.Exception,
                        "Handler failed, retry {Attempt} in {Delay}", args.AttemptNumber + 1, args.RetryDelay);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    public async Task<ProcessingOutcome> ProcessAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        var correlationId = CorrelationContext.IsValid(message.CorrelationId) ? message.CorrelationId! : CorrelationContext.NewId();
        CorrelationContext.Current = correlationId;

        ActivityContext.TryParse(message.TraceParent, null, out var parent);
        using var activity = Telemetry.ActivitySource.StartActivity($"{message.MessageType} process", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation", "process");
        activity?.SetTag("messaging.message.id", message.MessageId);
        activity?.SetTag("correlation.id", correlationId);

        using (LogContext.PushProperty(CorrelationContext.LogPropertyName, correlationId))
        using (LogContext.PushProperty("MessageId", message.MessageId))
        using (LogContext.PushProperty("MessageType", message.MessageType))
        {
            var outcome = await ProcessCoreAsync(message, correlationId, cancellationToken);
            Processed.Add(1,
                new KeyValuePair<string, object?>("message.type", message.MessageType),
                new KeyValuePair<string, object?>("outcome", outcome.ToString()));
            activity?.SetTag("messaging.outcome", outcome.ToString());
            if (outcome is ProcessingOutcome.Rejected or ProcessingOutcome.Failed)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
            }

            return outcome;
        }
    }

    private async Task<ProcessingOutcome> ProcessCoreAsync(
        IncomingMessage message, string correlationId, CancellationToken cancellationToken)
    {
        if (message.MessageId == Guid.Empty)
        {
            _logger.LogError("Rejecting message without a valid message id");
            return ProcessingOutcome.Rejected;
        }

        if (!_registry.TryGet(message.MessageType, out var registration))
        {
            _logger.LogError("Rejecting message of unknown type {MessageType}", message.MessageType);
            return ProcessingOutcome.Rejected;
        }

        object payload;
        try
        {
            payload = JsonSerializer.Deserialize(message.Body.Span, registration.ClrType, MessagingJson.Options)
                      ?? throw new JsonException("Message body is null.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Rejecting message with an unreadable body");
            return ProcessingOutcome.Rejected;
        }

        var context = new MessageContext(message.MessageId, message.MessageType, correlationId);

        try
        {
            var handled = await _retry.ExecuteAsync(async token =>
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var inbox = scope.ServiceProvider.GetRequiredService<IInbox>();
                return await inbox.TryProcessAsync(
                    message.MessageId,
                    message.MessageType,
                    _consumer,
                    ct => registration.Invoke(scope.ServiceProvider, payload, context, ct),
                    token);
            }, cancellationToken);

            if (!handled)
            {
                _logger.LogInformation("Skipping duplicate message");
                return ProcessingOutcome.Duplicate;
            }

            _logger.LogDebug("Message processed");
            return ProcessingOutcome.Processed;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Message failed after all retries and will be dead-lettered");
            return ProcessingOutcome.Failed;
        }
    }
}
