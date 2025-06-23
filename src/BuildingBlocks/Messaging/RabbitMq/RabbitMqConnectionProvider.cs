using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;

namespace BuildingBlocks.Messaging.RabbitMq;

/// <summary>
/// Owns the single AMQP connection of the process. The first caller waits (with backoff)
/// until the broker is reachable; after that the client library's automatic recovery
/// re-establishes the connection, its channels and consumers.
/// </summary>
public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly MessagingOptions _options;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ResiliencePipeline _connectRetry;
    private IConnection? _connection;

    public RabbitMqConnectionProvider(IOptions<MessagingOptions> options, ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _connectRetry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = int.MaxValue,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(30),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => ex is not OperationCanceledException),
                OnRetry = args =>
                {
                    _logger.LogWarning("RabbitMQ at {Host}:{Port} not reachable ({Error}), retrying in {Delay}",
                        _options.Host, _options.Port, args.Outcome.Exception?.Message, args.RetryDelay);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    public bool IsConnected => _connection is { IsOpen: true };

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not null)
            {
                return _connection;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                ClientProvidedName = _options.QueueName,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            };

            _connection = await _connectRetry.ExecuteAsync(
                async token => await factory.CreateConnectionAsync(token), cancellationToken);

            _logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}", _options.Host, _options.Port);
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        _gate.Dispose();
    }
}
