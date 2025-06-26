using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Ordering.Api.Domain;

namespace Ordering.Api.Data;

public sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.ToTable("orders");
            order.HasKey(o => o.Id);
            order.Property(o => o.CustomerId).HasMaxLength(Order.MaxCustomerIdLength).IsRequired();
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            order.Property(o => o.Currency).HasMaxLength(3).IsFixedLength();
            order.Property(o => o.Total).HasPrecision(18, 2);
            order.Property(o => o.CancellationReason).HasMaxLength(500);
            order.HasIndex(o => new { o.CustomerId, o.CreatedAt });

            order.OwnsMany(o => o.Lines, line =>
            {
                line.ToTable("order_lines");
                line.WithOwner().HasForeignKey("OrderId");
                line.HasKey("OrderId", nameof(OrderLine.ProductId));
                line.Property(l => l.ProductName).HasMaxLength(200).IsRequired();
                line.Property(l => l.UnitPrice).HasPrecision(18, 2);
                line.Ignore(l => l.LineTotal);
            });
            order.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.AddMessageStore();
    }
}
