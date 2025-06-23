using BuildingBlocks.Inbox;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Persistence;

public static class MessageStoreModelBuilderExtensions
{
    /// <summary>Maps the outbox and inbox tables into a service's own DbContext.</summary>
    public static ModelBuilder AddMessageStore(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable("outbox_messages");
            outbox.HasKey(m => m.Id);
            outbox.Property(m => m.Type).HasMaxLength(200).IsRequired();
            outbox.Property(m => m.Payload).IsRequired();
            outbox.Property(m => m.CorrelationId).HasMaxLength(128);
            outbox.Property(m => m.TraceParent).HasMaxLength(128);
            outbox.Property(m => m.LastError).HasMaxLength(2000);
            outbox.HasIndex(m => new { m.ProcessedOnUtc, m.OccurredOnUtc });
        });

        modelBuilder.Entity<InboxMessage>(inbox =>
        {
            inbox.ToTable("inbox_messages");
            inbox.HasKey(m => new { m.MessageId, m.Consumer });
            inbox.Property(m => m.Consumer).HasMaxLength(200);
            inbox.Property(m => m.MessageType).HasMaxLength(200).IsRequired();
            inbox.HasIndex(m => m.ProcessedOnUtc);
        });

        return modelBuilder;
    }
}
