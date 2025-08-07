using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Shipping.Api.Domain;

namespace Shipping.Api.Data;

public sealed class ShippingDbContext(DbContextOptions<ShippingDbContext> options) : DbContext(options)
{
    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyProviderConventions(Database);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shipment>(shipment =>
        {
            shipment.ToTable("shipments");
            shipment.HasKey(s => s.Id);
            shipment.HasIndex(s => s.OrderId).IsUnique();
            shipment.HasIndex(s => s.TrackingNumber).IsUnique();
            shipment.Property(s => s.CustomerId).HasMaxLength(100).IsRequired();
            shipment.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            shipment.Property(s => s.Carrier).HasMaxLength(100);
            shipment.Property(s => s.TrackingNumber).HasMaxLength(40);
            shipment.Property(s => s.FailureReason).HasMaxLength(500);
            shipment.Ignore(s => s.IsBooked);

            shipment.OwnsMany(s => s.Lines, line =>
            {
                line.ToTable("shipment_lines");
                line.WithOwner().HasForeignKey("ShipmentId");
                line.HasKey("ShipmentId", nameof(ShipmentLine.ProductId));
            });
            shipment.Navigation(s => s.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

            shipment.OwnsMany(s => s.Events, tracking =>
            {
                tracking.ToTable("shipment_events");
                tracking.WithOwner().HasForeignKey("ShipmentId");
                tracking.Property<int>("Id");
                tracking.HasKey("Id");
                tracking.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
                tracking.Property(e => e.Note).HasMaxLength(500);
            });
            shipment.Navigation(s => s.Events).UsePropertyAccessMode(PropertyAccessMode.Field);

            // PostgreSQL xmin: the saga's CancelShipment and an operator's dispatch cannot both win.
            shipment.HasRowVersion(Database);
        });

        modelBuilder.AddMessageStore();
    }
}
