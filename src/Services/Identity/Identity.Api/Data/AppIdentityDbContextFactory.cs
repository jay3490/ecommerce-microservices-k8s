using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Identity.Api.Data;

/// <summary>
/// Used only by <c>dotnet ef</c> to create migrations without starting the web host.
/// </summary>
internal sealed class AppIdentityDbContextFactory : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=identity;Username=postgres;Password=postgres")
            .Options;

        return new AppIdentityDbContext(options);
    }
}
