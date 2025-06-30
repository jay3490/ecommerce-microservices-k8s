using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Inventory.Api.Data;

/// <summary>
/// Used only by <c>dotnet ef</c> to create migrations without starting the web host.
/// </summary>
internal sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql("Host=localhost;Database=inventory;Username=postgres;Password=postgres")
            .Options;

        return new InventoryDbContext(options);
    }
}
