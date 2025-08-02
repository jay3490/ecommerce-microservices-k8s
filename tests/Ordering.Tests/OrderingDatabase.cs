using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ordering.Api.Data;

namespace Ordering.Tests;

/// <summary>
/// An in-memory SQLite database with the ordering schema, shared by every context created
/// from it, so handlers can be run the way the inbox runs them: change, then one SaveChanges.
/// </summary>
public sealed class OrderingDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public OrderingDatabase()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public OrderingDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<OrderingDbContext>().UseSqlite(_connection).Options);

    /// <summary>Messages enqueued on the outbox and committed, oldest first.</summary>
    public List<OutboxMessage> Outbox()
    {
        using var db = CreateContext();
        return db.Set<OutboxMessage>().AsNoTracking().AsEnumerable().OrderBy(m => m.OccurredOnUtc).ToList();
    }

    public List<string> OutboxTypes() => Outbox().Select(m => m.Type).ToList();

    public void Dispose() => _connection.Dispose();
}

public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

public static class TestContext
{
    public static MessageContext For<TMessage>() where TMessage : IIntegrationMessage =>
        new(Guid.NewGuid(), MessageType.NameOf<TMessage>(), "corr-test");
}
