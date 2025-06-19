using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Catalog.Api.Data;

/// <summary>
/// Used only by <c>dotnet ef</c> to create migrations without starting the web host.
/// </summary>
internal sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=localhost;Database=catalog;Username=postgres;Password=postgres")
            .Options;

        return new CatalogDbContext(options);
    }
}
