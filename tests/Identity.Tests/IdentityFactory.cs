using System.Net.Http.Json;
using BuildingBlocks.Messaging;
using Identity.Api.Accounts;
using Identity.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Identity.Tests;

/// <summary>
/// Runs Identity.Api in memory on an in-memory SQLite database. The schema is created before
/// the host starts because the program seeds roles and the admin account on startup.
/// Published messages go to <see cref="Published"/> instead of RabbitMQ.
/// </summary>
public sealed class IdentityFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin-Passw0rd-123";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public IdentityFactory()
    {
        _connection.Open();
        using var context = new AppIdentityDbContext(DbOptions());
        context.Database.EnsureCreated();
    }

    public RecordingPublisher Published { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Identity:Issuer", "https://identity.test");
        builder.UseSetting("Identity:AllowEphemeralSigningKey", "true");
        builder.UseSetting("Identity:SeedAdmin:Email", AdminEmail);
        builder.UseSetting("Identity:SeedAdmin:Password", AdminPassword);
        builder.UseSetting("Outbox:PollingInterval", "00:00:00.100");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
            services.AddSingleton(DbOptions());
            services.RemoveAll<IMessagePublisher>();
            services.AddSingleton<IMessagePublisher>(Published);
        });
    }

    public AppIdentityDbContext CreateDbContext() => new(DbOptions());

    public async Task<TokenResponse> RegisterAsync(HttpClient client, string email, string password = "Correct-Horse-9")
    {
        var response = await client.PostAsJsonAsync("/api/identity/register", new { email, password, displayName = "Test User" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }

    private DbContextOptions<AppIdentityDbContext> DbOptions() =>
        new DbContextOptionsBuilder<AppIdentityDbContext>().UseSqlite(_connection).Options;
}

public sealed class RecordingPublisher : IMessagePublisher
{
    private readonly List<OutgoingMessage> _published = [];

    public IReadOnlyList<OutgoingMessage> Messages
    {
        get
        {
            lock (_published)
            {
                return _published.ToList();
            }
        }
    }

    public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        lock (_published)
        {
            _published.Add(message);
        }

        return Task.CompletedTask;
    }
}
