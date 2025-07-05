using System.Net;
using System.Net.Http.Json;

namespace Gateway.Tests;

public sealed class GatewayPipelineTests
{
    [Fact]
    public async Task Orders_require_a_token()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_customer_token_is_let_through_to_the_ordering_cluster()
    {
        using var factory = new GatewayFactory();
        using var client = await factory.CreateClientWithTokenAsync("customer-42", "customer");

        var response = await client.GetAsync("/api/orders");

        // Authenticated and authorized; the (absent) downstream service yields 502.
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Changing_the_catalog_requires_the_admin_role()
    {
        using var factory = new GatewayFactory();
        using var customer = await factory.CreateClientWithTokenAsync("customer-42", "customer");
        using var admin = await factory.CreateClientWithTokenAsync("ops-1", "admin");
        var product = new { sku = "X-1", name = "Thing", price = 1.0m };

        var asCustomer = await customer.PostAsJsonAsync("/api/products", product);
        var asAdmin = await admin.PostAsJsonAsync("/api/products", product);

        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, asAdmin.StatusCode);
    }

    [Fact]
    public async Task Catalog_reads_are_public()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_rejected()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJtYWxsb3J5In0.c2lnbmF0dXJlLXRoYXQtZG9lcy1ub3QtbWF0Y2g");

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Clients_over_the_limit_get_429_with_retry_after()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < GatewayFactory.PermitLimit; i++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/products")).StatusCode);
        }

        var limited = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter is not null, "Expected a Retry-After header");
    }

    [Fact]
    public async Task Limits_are_tracked_per_authenticated_subject()
    {
        using var factory = new GatewayFactory();
        using var first = await factory.CreateClientWithTokenAsync("customer-1", "customer");
        using var second = await factory.CreateClientWithTokenAsync("customer-2", "customer");

        for (var i = 0; i < GatewayFactory.PermitLimit; i++)
        {
            await first.GetAsync("/api/orders");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, (await second.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task Correlation_ids_are_echoed_or_created()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        using var withId = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        withId.Headers.Add("X-Correlation-ID", "abc-123");
        var echoed = await client.SendAsync(withId);
        var created = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, echoed.StatusCode);
        Assert.Equal("abc-123", Assert.Single(echoed.Headers.GetValues("X-Correlation-ID")));
        Assert.Equal(32, Assert.Single(created.Headers.GetValues("X-Correlation-ID")).Length);
    }

    [Fact]
    public async Task The_dev_token_endpoint_rejects_unknown_roles()
    {
        using var factory = new GatewayFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/dev-token", new { subject = "x", roles = new[] { "root" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
