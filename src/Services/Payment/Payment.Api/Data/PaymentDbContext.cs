using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;

namespace Payment.Api.Data;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentRecord>(payment =>
        {
            payment.ToTable("payments");
            payment.HasKey(p => p.Id);
            payment.HasIndex(p => p.OrderId).IsUnique();
            payment.Property(p => p.CustomerId).HasMaxLength(100).IsRequired();
            payment.Property(p => p.Amount).HasPrecision(18, 2);
            payment.Property(p => p.Currency).HasMaxLength(3).IsFixedLength();
            payment.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            payment.Property(p => p.DeclineReason).HasMaxLength(500);
        });

        modelBuilder.AddMessageStore();
    }
}
