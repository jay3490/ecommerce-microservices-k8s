using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Outbox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shipping.Api.Data;
using Shipping.Api.Domain;
using Shipping.Api.Shipments;

namespace Shipping.Tests;

public sealed class ShippingFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ShippingFactory()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public ShippingDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ShippingDbContext>().UseSqlite(_connection).Options);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ShippingDbContext>>();
            services.AddSingleton(new DbContextOptionsBuilder<ShippingDbContext>().UseSqlite(_connection).Options);

            // No broker in these tests: keep the consumer and the outbox dispatcher from starting.
            services.RemoveAll<IHostedService>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

public sealed class ShipmentTrackingApiTests : IClassFixture<ShippingFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);
    private readonly ShippingFactory _factory;

    public ShipmentTrackingApiTests(ShippingFactory factory) => _factory = factory;

    [Fact]
    public async Task Customers_see_the_tracking_history_of_their_own_shipments_only()
    {
        var orderId = await BookAsync("owner-1");
        using var owner = Client("owner-1");
        using var stranger = Client("someone-else");

        var shipment = await owner.GetFromJsonAsync<ShipmentDto>($"/api/shipments/{orderId}");

        Assert.Equal("Pending", shipment!.Status);
        Assert.Equal("Pending", Assert.Single(shipment.History).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/shipments/{orderId}")).StatusCode);
    }

    [Fact]
    public async Task Dispatch_then_delivery_publish_tracking_events()
    {
        var orderId = await BookAsync("owner-2");
        using var client = _factory.CreateClient();

        var dispatched = await client.PostAsync($"/api/shipments/{orderId}/dispatch", null);
        var delivered = await client.PostAsync($"/api/shipments/{orderId}/deliver", null);

        Assert.Equal(HttpStatusCode.OK, dispatched.StatusCode);
        var shipment = (await delivered.Content.ReadFromJsonAsync<ShipmentDto>())!;
        Assert.Equal(["Pending", "Dispatched", "Delivered"], shipment.History.Select(h => h.Status));

        await using var db = _factory.CreateDb();
        var types = await db.Set<OutboxMessage>().Where(m => m.Payload.Contains(orderId.ToString())).Select(m => m.Type).ToListAsync();
        Assert.Equal(["ShipmentDelivered", "ShipmentDispatched"], types.Order());
    }

    [Fact]
    public async Task Status_changes_out_of_order_are_conflicts()
    {
        var orderId = await BookAsync("owner-3");
        using var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/shipments/{orderId}/deliver", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/shipments/{Guid.NewGuid()}/dispatch", null)).StatusCode);
    }

    private async Task<Guid> BookAsync(string customerId)
    {
        var orderId = Guid.NewGuid();
        await using var db = _factory.CreateDb();
        db.Shipments.Add(Shipment.Book(orderId, customerId, [new ShipmentLine(Guid.NewGuid(), 1)], "Test Carrier",
            ShippingPolicy.NewTrackingNumber(), Now));
        await db.SaveChangesAsync();
        return orderId;
    }

    private HttpClient Client(string customerId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Customer-Id", customerId);
        return client;
    }
}
