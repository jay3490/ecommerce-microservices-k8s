using BuildingBlocks.Persistence;
using Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<StockReservation> Reservations => Set<StockReservation>();

    /// <summary>Loads (tracked) the stock rows for the given products, keyed by product id.</summary>
    public async Task<Dictionary<Guid, StockItem>> LoadStockAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.Distinct().ToList();
        return await StockItems.Where(s => ids.Contains(s.ProductId)).ToDictionaryAsync(s => s.ProductId, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockItem>(stock =>
        {
            stock.HasKey(s => s.ProductId);
            stock.ToTable("stock_items", table =>
            {
                table.HasCheckConstraint("CK_stock_items_available", "\"Available\" >= 0");
                table.HasCheckConstraint("CK_stock_items_reserved", "\"Reserved\" >= 0");
            });

            // Two orders racing for the same product: the second commit fails on xmin and
            // its handler is retried against the new quantities.
            stock.Property<uint>("Version").IsRowVersion();

            stock.HasData(StockSeed.Items);
        });

        modelBuilder.Entity<StockReservation>(reservation =>
        {
            reservation.ToTable("stock_reservations");
            reservation.HasKey(r => r.Id);
            reservation.HasIndex(r => r.OrderId).IsUnique();
            reservation.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            reservation.OwnsMany(r => r.Lines, line =>
            {
                line.ToTable("stock_reservation_lines");
                line.WithOwner().HasForeignKey("ReservationId");
                line.HasKey("ReservationId", nameof(ReservationLine.ProductId));
            });
            reservation.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.AddMessageStore();
    }
}
