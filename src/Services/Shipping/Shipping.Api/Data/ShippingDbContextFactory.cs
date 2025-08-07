using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shipping.Api.Data;

/// <summary>
/// Used only by <c>dotnet ef</c> to create migrations without starting the web host.
/// </summary>
internal sealed class ShippingDbContextFactory : IDesignTimeDbContextFactory<ShippingDbContext>
{
    public ShippingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShippingDbContext>()
            .UseNpgsql("Host=localhost;Database=shipping;Username=postgres;Password=postgres")
            .Options;

        return new ShippingDbContext(options);
    }
}
