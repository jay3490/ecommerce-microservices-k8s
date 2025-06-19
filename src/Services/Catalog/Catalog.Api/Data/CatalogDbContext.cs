using Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Data;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("products");
            product.HasKey(p => p.Id);
            product.Property(p => p.Sku).HasMaxLength(Product.MaxSkuLength).IsRequired();
            product.HasIndex(p => p.Sku).IsUnique();
            product.Property(p => p.Name).HasMaxLength(Product.MaxNameLength).IsRequired();
            product.Property(p => p.Description).HasMaxLength(Product.MaxDescriptionLength);
            product.Property(p => p.Price).HasPrecision(18, 2);
            product.Property(p => p.Currency).HasMaxLength(3).IsFixedLength();

            product.HasData(ProductSeed.Products);
        });
    }
}
