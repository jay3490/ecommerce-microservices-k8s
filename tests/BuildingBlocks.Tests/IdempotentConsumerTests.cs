using System.Text;
using System.Text.Json;
using BuildingBlocks.Inbox;
using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Tests;

public sealed class IdempotentConsumerTests : IDisposable
{
    private const string Consumer = "widgets";

    private readonly SqliteDatabase _database = new();
    private readonly HandlerBehaviour _behaviour = new();
    private readonly ServiceProvider _services;
    private readonly MessageProcessor _processor;

    public IdempotentConsumerTests()
    {
        var registry = new MessageHandlerRegistry();
        registry.Register<WidgetCreated>();

        var services = new ServiceCollection();
        services.AddSingleton(_behaviour);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));
        services.AddSingleton<OutboxSignal>();
        services.AddScoped(_ => _database.CreateContext());
        services.AddScoped<IInbox, EntityFrameworkInbox<TestDbContext>>();
        services.AddScoped<IOutbox, EntityFrameworkOutbox<TestDbContext>>();
        services.AddScoped<IMessageHandler<WidgetCreated>, CreateWidgetHandler>();
        _services = services.BuildServiceProvider();

        var options = Options.Create(new MessagingOptions
        {
            QueueName = Consumer,
            RetryCount = 2,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1),
        });
        _processor = new MessageProcessor(
            _services.GetRequiredService<IServiceScopeFactory>(), registry, options, NullLogger<MessageProcessor>.Instance);
    }

    [Fact]
    public async Task A_redelivered_message_is_handled_only_once()
    {
        var message = Incoming(new WidgetCreated(Guid.NewGuid(), "gear"));

        var first = await _processor.ProcessAsync(message, CancellationToken.None);
        var second = await _processor.ProcessAsync(message, CancellationToken.None);

        Assert.Equal(ProcessingOutcome.Processed, first);
        Assert.Equal(ProcessingOutcome.Duplicate, second);
        Assert.Equal(1, _behaviour.Invocations);
        await using var verify = _database.CreateContext();
        Assert.Single(await verify.Widgets.ToListAsync());
        var record = Assert.Single(await verify.Set<InboxMessage>().ToListAsync());
        Assert.Equal((message.MessageId, Consumer), (record.MessageId, record.Consumer));
    }

    [Fact]
    public async Task Different_messages_with_the_same_content_are_both_handled()
    {
        var payload = new WidgetCreated(Guid.NewGuid(), "gear");

        await _processor.ProcessAsync(Incoming(payload), CancellationToken.None);
        await _processor.ProcessAsync(Incoming(payload with { WidgetId = Guid.NewGuid() }), CancellationToken.None);

        Assert.Equal(2, _behaviour.Invocations);
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_committed_once()
    {
        _behaviour.FailuresBeforeSuccess = 1;

        var outcome = await _processor.ProcessAsync(Incoming(new WidgetCreated(Guid.NewGuid(), "gear")), CancellationToken.None);

        Assert.Equal(ProcessingOutcome.Processed, outcome);
        Assert.Equal(2, _behaviour.Invocations);
        await using var verify = _database.CreateContext();
        Assert.Single(await verify.Widgets.ToListAsync());
    }

    [Fact]
    public async Task A_handler_that_keeps_failing_leaves_no_trace_and_is_reported_as_failed()
    {
        _behaviour.FailuresBeforeSuccess = int.MaxValue;
        var message = Incoming(new WidgetCreated(Guid.NewGuid(), "gear"));

        var outcome = await _processor.ProcessAsync(message, CancellationToken.None);

        Assert.Equal(ProcessingOutcome.Failed, outcome);
        Assert.Equal(3, _behaviour.Invocations); // first attempt + 2 retries
        await using var verify = _database.CreateContext();
        Assert.Empty(await verify.Widgets.ToListAsync());
        Assert.Empty(await verify.Set<InboxMessage>().ToListAsync());
        Assert.Empty(await verify.Set<OutboxMessage>().ToListAsync());

        // The message was not marked as processed, so a later redelivery still runs it.
        _behaviour.FailuresBeforeSuccess = 0;
        Assert.Equal(ProcessingOutcome.Processed, await _processor.ProcessAsync(message, CancellationToken.None));
    }

    [Fact]
    public async Task Outgoing_messages_are_committed_together_with_the_inbox_record()
    {
        var widgetId = Guid.NewGuid();

        await _processor.ProcessAsync(Incoming(new WidgetCreated(widgetId, "gear")), CancellationToken.None);

        await using var verify = _database.CreateContext();
        var outgoing = Assert.Single(await verify.Set<OutboxMessage>().ToListAsync());
        Assert.Equal(nameof(WidgetRenamed), outgoing.Type);
        Assert.Equal("correlation-1", outgoing.CorrelationId);
        Assert.Single(await verify.Set<InboxMessage>().ToListAsync());
    }

    [Fact]
    public async Task Losing_a_race_with_a_concurrent_delivery_counts_as_a_duplicate()
    {
        var messageId = Guid.NewGuid();
        await using var context = _database.CreateContext();
        var inbox = new EntityFrameworkInbox<TestDbContext>(context, TimeProvider.System);

        var handled = await inbox.TryProcessAsync(messageId, nameof(WidgetCreated), Consumer, async ct =>
        {
            context.Widgets.Add(new Widget { Id = Guid.NewGuid(), Name = "loser" });

            // Another replica commits the same message while this handler is still running.
            await using var other = _database.CreateContext();
            other.Set<InboxMessage>().Add(new InboxMessage
            {
                MessageId = messageId,
                Consumer = Consumer,
                MessageType = nameof(WidgetCreated),
                ProcessedOnUtc = DateTime.UtcNow,
            });
            await other.SaveChangesAsync(ct);
        }, CancellationToken.None);

        Assert.False(handled);
        await using var verify = _database.CreateContext();
        Assert.Empty(await verify.Widgets.ToListAsync());
    }

    [Fact]
    public async Task Unknown_message_types_are_rejected_without_running_a_handler()
    {
        var message = Incoming(new WidgetRenamed(Guid.NewGuid(), "gear"));

        var outcome = await _processor.ProcessAsync(message, CancellationToken.None);

        Assert.Equal(ProcessingOutcome.Rejected, outcome);
        Assert.Equal(0, _behaviour.Invocations);
    }

    [Fact]
    public async Task Unreadable_bodies_and_missing_ids_are_rejected()
    {
        var garbage = new IncomingMessage(Guid.NewGuid(), nameof(WidgetCreated), Encoding.UTF8.GetBytes("not json"), null, null);
        var noId = Incoming(new WidgetCreated(Guid.NewGuid(), "gear")) with { MessageId = Guid.Empty };

        Assert.Equal(ProcessingOutcome.Rejected, await _processor.ProcessAsync(garbage, CancellationToken.None));
        Assert.Equal(ProcessingOutcome.Rejected, await _processor.ProcessAsync(noId, CancellationToken.None));
        Assert.Equal(0, _behaviour.Invocations);
    }

    private static IncomingMessage Incoming<TMessage>(TMessage message) where TMessage : IIntegrationMessage =>
        new(
            Guid.NewGuid(),
            typeof(TMessage).Name,
            JsonSerializer.SerializeToUtf8Bytes(message, MessagingJson.Options),
            "correlation-1",
            null);

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    public sealed class HandlerBehaviour
    {
        public int Invocations { get; set; }
        public int FailuresBeforeSuccess { get; set; }
    }

    private sealed class CreateWidgetHandler(TestDbContext db, IOutbox outbox, HandlerBehaviour behaviour)
        : IMessageHandler<WidgetCreated>
    {
        public Task HandleAsync(WidgetCreated message, MessageContext context, CancellationToken cancellationToken)
        {
            behaviour.Invocations++;

            // Changes are made before the failure so the test proves they are discarded.
            db.Widgets.Add(new Widget { Id = message.WidgetId, Name = message.Name });
            outbox.Enqueue(new WidgetRenamed(message.WidgetId, message.Name.ToUpperInvariant()));

            if (behaviour.Invocations <= behaviour.FailuresBeforeSuccess)
            {
                throw new InvalidOperationException("Simulated handler failure");
            }

            return Task.CompletedTask;
        }
    }
}
