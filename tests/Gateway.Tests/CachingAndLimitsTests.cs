using System.Net;
using System.Net.Http.Json;
using Gateway.Limits;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Gateway.Tests;

/// <summary>
/// A stand-in catalog service on a real local port, so the gateway proxies to something that
/// answers. It counts how often each path is hit.
/// </summary>
public sealed class FakeCatalogService : IAsyncDisposable
{
    public const int LargeBodyBytes = 64 * 1024;

    private readonly WebApplication _app;
    private int _productReads;

    private FakeCatalogService(WebApplication app) => _app = app;

    public int ProductReads => Volatile.Read(ref _productReads);

    public string Address => _app.Urls.First();

    public static async Task<FakeCatalogService> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var service = new FakeCatalogService(app);

        app.MapGet("/api/products", (HttpContext context) =>
        {
            Interlocked.Increment(ref service._productReads);
            return Results.Json(new { reads = service.ProductReads, query = context.Request.QueryString.Value });
        });
        app.MapPost("/api/products", () => Results.Created("/api/products/1", new { id = 1 }));
        app.MapGet("/api/products/large", () => Results.Bytes(new byte[LargeBodyBytes], "application/octet-stream"));

        await app.StartAsync();
        return service;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public sealed class CatalogGatewayFactory(string catalogAddress) : GatewayFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ReverseProxy:Clusters:catalog:Destinations:primary:Address", catalogAddress);
        builder.UseSetting("RateLimiting:PermitLimit", "1000");
        builder.UseSetting("Gateway:Limits:MaxRequestBodyBytes", "1024");
        builder.UseSetting("Gateway:Limits:MaxResponseBodyBytes", (FakeCatalogService.LargeBodyBytes / 2).ToString());
    }
}

public sealed class CachingAndLimitsTests : IAsyncLifetime
{
    private FakeCatalogService _catalog = null!;
    private CatalogGatewayFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _catalog = await FakeCatalogService.StartAsync();
        _factory = new CatalogGatewayFactory(_catalog.Address);
    }

    [Fact]
    public async Task Anonymous_catalog_reads_are_served_from_the_cache()
    {
        using var client = _factory.CreateClient();

        var first = await client.GetStringAsync("/api/products");
        var second = await client.GetStringAsync("/api/products");

        Assert.Equal(first, second);
        Assert.Equal(1, _catalog.ProductReads);
    }

    [Fact]
    public async Task Different_query_strings_are_cached_separately()
    {
        using var client = _factory.CreateClient();

        await client.GetStringAsync("/api/products?page=1");
        await client.GetStringAsync("/api/products?page=2");
        await client.GetStringAsync("/api/products?page=1");

        Assert.Equal(2, _catalog.ProductReads);
    }

    [Fact]
    public async Task Requests_with_a_token_bypass_the_cache()
    {
        using var client = await _factory.CreateClientWithTokenAsync("customer-1", "customer");

        await client.GetStringAsync("/api/products");
        await client.GetStringAsync("/api/products");

        Assert.Equal(2, _catalog.ProductReads);
    }

    [Fact]
    public async Task A_product_change_through_the_gateway_evicts_cached_reads()
    {
        using var anonymous = _factory.CreateClient();
        using var admin = await _factory.CreateClientWithTokenAsync("ops-1", "admin");

        await anonymous.GetStringAsync("/api/products");
        var write = await admin.PostAsJsonAsync("/api/products", new { sku = "X", name = "X", price = 1m });
        await anonymous.GetStringAsync("/api/products");

        Assert.Equal(HttpStatusCode.Created, write.StatusCode);
        Assert.Equal(2, _catalog.ProductReads);
    }

    [Fact]
    public async Task Request_bodies_over_the_limit_are_refused_before_they_are_forwarded()
    {
        using var admin = await _factory.CreateClientWithTokenAsync("ops-1", "admin");

        var response = await admin.PostAsync("/api/products", new ByteArrayContent(new byte[2048]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Responses_over_the_limit_are_not_relayed()
    {
        using var client = await _factory.CreateClientWithTokenAsync("customer-1", "customer");

        var response = await client.GetAsync("/api/products/large");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task The_response_stream_stops_writes_past_the_limit()
    {
        using var inner = new MemoryStream();
        await using var limited = new ResponseSizeLimitStream(inner, 10);

        await limited.WriteAsync(new byte[10]);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await limited.WriteAsync(new byte[1]));
        Assert.Equal(10, inner.Length);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _catalog.DisposeAsync();
    }
}
