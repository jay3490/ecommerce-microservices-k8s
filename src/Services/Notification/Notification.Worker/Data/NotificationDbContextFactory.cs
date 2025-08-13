using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Notification.Worker.Data;

/// <summary>
/// Used only by <c>dotnet ef</c> to create migrations without starting the web host.
/// </summary>
internal sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql("Host=localhost;Database=notification;Username=postgres;Password=postgres")
            .Options;

        return new NotificationDbContext(options);
    }
}
