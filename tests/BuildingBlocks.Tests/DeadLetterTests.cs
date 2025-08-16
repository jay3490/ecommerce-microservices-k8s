using System.Text;
using BuildingBlocks.Messaging.DeadLetters;
using DeadLetters.Cli;

namespace BuildingBlocks.Tests;

/// <summary>
/// A dead-letter queue with RabbitMQ's semantics: fetched messages are held by the session,
/// acknowledged ones are gone, and everything else returns to the queue when it ends.
/// </summary>
public sealed class FakeDeadLetterQueue : IDeadLetterQueue
{
    private ulong _nextTag;

    public List<RawDeadLetter> Messages { get; } = [];
    public List<RawDeadLetter> Republished { get; } = [];
    public HashSet<string> FailRepublishFor { get; } = [];
    public int OpenSessions { get; private set; }

    public string Name => "ordering.dead-letter";

    public RawDeadLetter Add(string messageId, string type = "StockReserved", string body = "{}", Dictionary<string, object?>? headers = null)
    {
        var message = new RawDeadLetter(++_nextTag, messageId, type, "corr-1", headers ?? [], Encoding.UTF8.GetBytes(body));
        Messages.Add(message);
        return message;
    }

    public Task<IDeadLetterSession> OpenAsync(CancellationToken cancellationToken)
    {
        OpenSessions++;
        return Task.FromResult<IDeadLetterSession>(new Session(this));
    }

    private sealed class Session(FakeDeadLetterQueue queue) : IDeadLetterSession
    {
        private int _position;

        public Task<RawDeadLetter?> NextAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_position < queue.Messages.Count ? queue.Messages[_position++] : null);

        public Task RepublishAsync(RawDeadLetter message, CancellationToken cancellationToken)
        {
            if (message.MessageId is not null && queue.FailRepublishFor.Contains(message.MessageId))
            {
                throw new InvalidOperationException("Broker did not confirm");
            }

            queue.Republished.Add(message);
            return Task.CompletedTask;
        }

        public Task AcknowledgeAsync(RawDeadLetter message, CancellationToken cancellationToken)
        {
            queue.Messages.Remove(message);
            _position--;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            queue.OpenSessions--;
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class DeadLetterTests
{
    private readonly FakeDeadLetterQueue _queue = new();
    private readonly DeadLetterService _service;

    public DeadLetterTests() => _service = new DeadLetterService(_queue);

    [Fact]
    public async Task Listing_shows_messages_without_removing_them()
    {
        _queue.Add("m-1");
        _queue.Add("m-2");

        var listed = await _service.ListAsync(10, CancellationToken.None);

        Assert.Equal(["m-1", "m-2"], listed.Select(m => m.MessageId));
        Assert.Equal(2, _queue.Messages.Count);
        Assert.Equal(0, _queue.OpenSessions);
    }

    [Fact]
    public async Task Listing_reads_the_dead_letter_reason_time_and_count()
    {
        var diedAt = new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);
        _queue.Add("m-1", headers: new Dictionary<string, object?>
        {
            ["x-death"] = new List<object?>
            {
                new Dictionary<string, object?> { ["reason"] = Encoding.UTF8.GetBytes("rejected"), ["time"] = diedAt, ["count"] = 2L },
            },
            [DeadLetterHeaders.ReplayCount] = 1,
        });

        var message = Assert.Single(await _service.ListAsync(10, CancellationToken.None));

        Assert.Equal(("rejected", diedAt, 2L, 1), (message.Reason, message.DeadLetteredAt, message.DeathCount, message.ReplayCount));
    }

    [Fact]
    public async Task Long_bodies_are_cut_short_in_the_listing()
    {
        _queue.Add("m-1", body: new string('x', 2000));

        var message = Assert.Single(await _service.ListAsync(10, CancellationToken.None));

        Assert.Equal(503, message.BodyPreview.Length);
    }

    [Fact]
    public async Task Replaying_selected_ids_republishes_and_removes_only_those()
    {
        _queue.Add("m-1");
        _queue.Add("m-2");
        _queue.Add("m-3");

        var result = await _service.ReplayAsync(new HashSet<string> { "m-1", "m-3" }, 100, CancellationToken.None);

        Assert.Equal(new DeadLetterResult(2, 2, 0), result);
        Assert.Equal(["m-1", "m-3"], _queue.Republished.Select(m => m.MessageId));
        Assert.Equal(["m-2"], _queue.Messages.Select(m => m.MessageId));
    }

    [Fact]
    public async Task Replaying_everything_empties_the_queue()
    {
        _queue.Add("m-1");
        _queue.Add("m-2");

        var result = await _service.ReplayAsync(null, 100, CancellationToken.None);

        Assert.Equal(2, result.Processed);
        Assert.Empty(_queue.Messages);
    }

    [Fact]
    public async Task A_message_whose_republish_fails_stays_dead_lettered()
    {
        _queue.Add("m-1");
        _queue.Add("m-2");
        _queue.FailRepublishFor.Add("m-1");

        var result = await _service.ReplayAsync(null, 100, CancellationToken.None);

        Assert.Equal(new DeadLetterResult(2, 1, 1), result);
        Assert.Equal(["m-1"], _queue.Messages.Select(m => m.MessageId));
    }

    [Fact]
    public async Task The_limit_caps_how_many_messages_are_looked_at()
    {
        for (var i = 0; i < 5; i++)
        {
            _queue.Add($"m-{i}");
        }

        var result = await _service.ReplayAsync(null, 2, CancellationToken.None);

        Assert.Equal(2, result.Processed);
        Assert.Equal(3, _queue.Messages.Count);
    }

    [Fact]
    public async Task Discarding_deletes_without_republishing()
    {
        _queue.Add("m-1");
        _queue.Add("m-2");

        var result = await _service.DiscardAsync(new HashSet<string> { "m-2" }, 100, CancellationToken.None);

        Assert.Equal(1, result.Processed);
        Assert.Empty(_queue.Republished);
        Assert.Equal(["m-1"], _queue.Messages.Select(m => m.MessageId));
    }

    [Theory]
    [InlineData("list ordering --limit 5", DeadLetterCommand.List, 5, 0)]
    [InlineData("replay payment --all", DeadLetterCommand.Replay, 100, 0)]
    [InlineData("replay payment --id a --id b", DeadLetterCommand.Replay, 100, 2)]
    [InlineData("DISCARD shipping --id a", DeadLetterCommand.Discard, 100, 1)]
    public void The_cli_parses_its_commands(string args, DeadLetterCommand command, int limit, int ids)
    {
        var parsed = CommandLine.Parse(args.Split(' '));

        Assert.Equal(command, parsed.Command);
        Assert.Equal(limit, parsed.Limit);
        Assert.Equal(ids, parsed.MessageIds?.Count ?? 0);
    }

    [Theory]
    [InlineData("replay ordering")]
    [InlineData("replay ordering --all --id a")]
    [InlineData("discard ordering")]
    [InlineData("list ordering --all")]
    [InlineData("purge ordering")]
    [InlineData("list")]
    public void The_cli_rejects_ambiguous_or_incomplete_commands(string args)
    {
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(args.Split(' ')));
    }
}
