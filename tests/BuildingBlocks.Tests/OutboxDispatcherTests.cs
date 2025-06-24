using System.Text.Json;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Tests;

public sealed class OutboxDispatcherTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly RecordingPublisher _publisher = new();
    private readonly OutboxOptions _options = new() { BatchSize = 10, MaxAttempts = 3 };

    [Fact]
    public async Task Enqueued_messages_are_stored_only_when_the_unit_of_work_commits()
    {
        await using (var context = _database.CreateContext())
        {
            var outbox = new EntityFrameworkOutbox<TestDbContext>(context, new OutboxSignal(), _clock);
            outbox.Enqueue(new WidgetCreated(Guid.NewGuid(), "left unsaved"));
        }

        await using var verify = _database.CreateContext();
        Assert.Empty(await verify.Set<OutboxMessage>().ToListAsync());
    }

    [Fact]
    public async Task Enqueue_captures_type_payload_and_correlation_id()
    {
        var widgetId = Guid.NewGuid();
        CorrelationContext.Current = "corr-123";

        await using (var context = _database.CreateContext())
        {
            var outbox = new EntityFrameworkOutbox<TestDbContext>(context, new OutboxSignal(), _clock);
            outbox.Enqueue(new WidgetCreated(widgetId, "gear"));
            await context.SaveChangesAsync();
        }

        await using var verify = _database.CreateContext();
        var stored = Assert.Single(await verify.Set<OutboxMessage>().ToListAsync());
        Assert.Equal(nameof(WidgetCreated), stored.Type);
        Assert.Equal("corr-123", stored.CorrelationId);
        Assert.Null(stored.ProcessedOnUtc);
        Assert.Equal(new WidgetCreated(widgetId, "gear"), JsonSerializer.Deserialize<WidgetCreated>(stored.Payload, MessagingJson.Options));
    }

    [Fact]
    public async Task Committing_an_enqueued_message_signals_the_dispatcher()
    {
        var signal = new OutboxSignal();
        await using (var context = _database.CreateContext())
        {
            var outbox = new EntityFrameworkOutbox<TestDbContext>(context, signal, _clock);
            outbox.Enqueue(new WidgetCreated(Guid.NewGuid(), "gear"));
            await context.SaveChangesAsync();
        }

        var waited = Task.Run(() => signal.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None));
        var finished = await Task.WhenAny(waited, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(waited, finished);
    }

    [Fact]
    public async Task Publishes_pending_messages_in_order_and_marks_them_processed()
    {
        var first = await AddMessageAsync(nameof(WidgetCreated), Start.AddSeconds(1));
        var second = await AddMessageAsync(nameof(WidgetRenamed), Start.AddSeconds(2));
        var already = await AddMessageAsync(nameof(WidgetCreated), Start, processed: true);

        var published = await DispatchAsync();

        Assert.Equal(2, published);
        Assert.Equal([first, second], _publisher.Published.Select(m => m.MessageId));
        await using var verify = _database.CreateContext();
        var stored = await verify.Set<OutboxMessage>().ToDictionaryAsync(m => m.Id);
        Assert.Equal(_clock.Now.UtcDateTime, stored[first].ProcessedOnUtc);
        Assert.Equal(_clock.Now.UtcDateTime, stored[second].ProcessedOnUtc);
        Assert.DoesNotContain(_publisher.Published, m => m.MessageId == already);
    }

    [Fact]
    public async Task A_failed_publish_is_recorded_and_stops_the_batch_to_preserve_order()
    {
        var failing = await AddMessageAsync(nameof(WidgetCreated), Start.AddSeconds(1));
        var later = await AddMessageAsync(nameof(WidgetRenamed), Start.AddSeconds(2));
        _publisher.FailOnType = nameof(WidgetCreated);

        var published = await DispatchAsync();

        Assert.Equal(0, published);
        Assert.Empty(_publisher.Published);
        await using var verify = _database.CreateContext();
        var stored = await verify.Set<OutboxMessage>().ToDictionaryAsync(m => m.Id);
        Assert.Equal(1, stored[failing].Attempts);
        Assert.Equal("Broker unavailable", stored[failing].LastError);
        Assert.Null(stored[failing].ProcessedOnUtc);
        Assert.Null(stored[later].ProcessedOnUtc);
    }

    [Fact]
    public async Task A_message_is_retried_on_the_next_pass_and_given_up_after_max_attempts()
    {
        var poison = await AddMessageAsync(nameof(WidgetCreated), Start.AddSeconds(1));
        var next = await AddMessageAsync(nameof(WidgetRenamed), Start.AddSeconds(2));
        _publisher.FailOnType = nameof(WidgetCreated);

        for (var pass = 0; pass < _options.MaxAttempts; pass++)
        {
            await DispatchAsync();
        }

        Assert.Empty(_publisher.Published);

        // The poison message has used up its attempts, so it no longer blocks the next one.
        var published = await DispatchAsync();

        Assert.Equal(1, published);
        Assert.Equal(next, Assert.Single(_publisher.Published).MessageId);
        await using var verify = _database.CreateContext();
        var stored = await verify.Set<OutboxMessage>().SingleAsync(m => m.Id == poison);
        Assert.Equal(_options.MaxAttempts, stored.Attempts);
        Assert.Null(stored.ProcessedOnUtc);
    }

    [Fact]
    public async Task Dispatches_at_most_one_batch_per_pass()
    {
        for (var i = 0; i < _options.BatchSize + 3; i++)
        {
            await AddMessageAsync(nameof(WidgetCreated), Start.AddSeconds(i));
        }

        Assert.Equal(_options.BatchSize, await DispatchAsync());
        Assert.Equal(3, await DispatchAsync());
        Assert.Equal(0, await DispatchAsync());
    }

    private async Task<int> DispatchAsync()
    {
        await using var context = _database.CreateContext();
        var dispatcher = new OutboxDispatcher<TestDbContext>(
            context, _publisher, Options.Create(_options), _clock, NullLogger<OutboxDispatcher<TestDbContext>>.Instance);
        return await dispatcher.DispatchPendingAsync(CancellationToken.None);
    }

    private async Task<Guid> AddMessageAsync(string type, DateTimeOffset occurredOn, bool processed = false)
    {
        await using var context = _database.CreateContext();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = type,
            Payload = "{}",
            OccurredOnUtc = occurredOn.UtcDateTime,
            ProcessedOnUtc = processed ? occurredOn.UtcDateTime : null,
        };
        context.Add(message);
        await context.SaveChangesAsync();
        return message.Id;
    }

    public void Dispose()
    {
        CorrelationContext.Current = null;
        _database.Dispose();
    }
}
