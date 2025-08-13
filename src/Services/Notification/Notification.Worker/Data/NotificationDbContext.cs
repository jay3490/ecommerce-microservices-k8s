using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Notification.Worker.Data;

/// <summary>
/// Where to send a customer's emails, learned from <c>UserRegistered</c>. The worker keeps its
/// own copy instead of asking the identity service on every event.
/// </summary>
public sealed class Recipient
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>A record of every email handed to the sender, for support questions and auditing.</summary>
public sealed class SentNotification
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public Guid? OrderId { get; set; }
    public DateTimeOffset SentAt { get; set; }
}

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<Recipient> Recipients => Set<Recipient>();
    public DbSet<SentNotification> SentNotifications => Set<SentNotification>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyProviderConventions(Database);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Recipient>(recipient =>
        {
            recipient.ToTable("recipients");
            recipient.HasKey(r => r.UserId);
            recipient.Property(r => r.UserId).HasMaxLength(100);
            recipient.Property(r => r.Email).HasMaxLength(256).IsRequired();
            recipient.Property(r => r.DisplayName).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<SentNotification>(sent =>
        {
            sent.ToTable("sent_notifications");
            sent.HasKey(s => s.Id);
            sent.Property(s => s.UserId).HasMaxLength(100).IsRequired();
            sent.Property(s => s.Kind).HasMaxLength(50).IsRequired();
            sent.Property(s => s.To).HasMaxLength(256).IsRequired();
            sent.Property(s => s.Subject).HasMaxLength(200).IsRequired();
            sent.HasIndex(s => new { s.UserId, s.SentAt });
            sent.HasIndex(s => s.OrderId);
        });

        modelBuilder.AddMessageStore();
    }
}
