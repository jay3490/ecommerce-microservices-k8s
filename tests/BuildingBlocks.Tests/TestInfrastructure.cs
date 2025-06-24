using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Tests;

public sealed class Widget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddMessageStore();
}

/// <summary>
/// An in-memory SQLite database that lives as long as the connection, so it behaves like a
/// real relational store (transactions, primary keys) while every DbContext shares it.
/// </summary>
public sealed class SqliteDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public SqliteDatabase()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public DbContextOptions<TestDbContext> Options => new DbContextOptionsBuilder<TestDbContext>()
        .UseSqlite(_connection)
        .Options;

    public TestDbContext CreateContext() => new(Options);

    public void Dispose() => _connection.Dispose();
}

public sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class RecordingPublisher : IMessagePublisher
{
    public List<OutgoingMessage> Published { get; } = [];

    /// <summary>Messages of this type throw instead of being published.</summary>
    public string? FailOnType { get; set; }

    public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        if (message.MessageType == FailOnType)
        {
            throw new InvalidOperationException("Broker unavailable");
        }

        Published.Add(message);
        return Task.CompletedTask;
    }
}

public sealed record WidgetCreated(Guid WidgetId, string Name) : IIntegrationMessage;

public sealed record WidgetRenamed(Guid WidgetId, string Name) : IIntegrationMessage;
