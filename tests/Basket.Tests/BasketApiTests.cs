using System.Net;
using System.Net.Http.Json;
using Basket.Api.Baskets;
using Basket.Api.Catalog;
using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basket.Tests;

public sealed class BasketFactory : WebApplicationFactory<Program>
{
    public InMemoryBasketStore Store { get; } = new();
    public FakePricing Pricing { get; } = new();
    public FakePublisher Publisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBasketStore>();
            services.AddSingleton<IBasketStore>(Store);
            services.RemoveAll<IProductPricing>();
            services.AddSingleton<IProductPricing>(Pricing);
            services.RemoveAll<IMessagePublisher>();
            services.AddSingleton<IMessagePublisher>(Publisher);
        });
    }

    public HttpClient CreateCustomerClient(string customerId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Customer-Id", customerId);
        return client;
    }
}

public sealed class BasketApiTests : IClassFixture<BasketFactory>
{
    private readonly BasketFactory _factory;

    public BasketApiTests(BasketFactory factory) => _factory = factory;

    [Fact]
    public async Task Customers_build_up_a_basket_item_by_item()
    {
        using var client = _factory.CreateCustomerClient("api-customer-1");
        var keyboard = Guid.NewGuid();
        var mouse = Guid.NewGuid();

        await client.PutAsJsonAsync($"/api/basket/items/{keyboard}", new { quantity = 2 });
        await client.PutAsJsonAsync($"/api/basket/items/{mouse}", new { quantity = 1 });
        await client.DeleteAsync($"/api/basket/items/{mouse}");
        var basket = await client.GetFromJsonAsync<BasketDto>("/api/basket");

        Assert.Equal([new(keyboard, 2)], basket!.Items);
    }

    [Fact]
    public async Task Baskets_are_kept_per_customer()
    {
        using var first = _factory.CreateCustomerClient("api-customer-2");
        using var second = _factory.CreateCustomerClient("api-customer-3");

        await first.PutAsJsonAsync($"/api/basket/items/{Guid.NewGuid()}", new { quantity = 1 });

        Assert.Empty((await second.GetFromJsonAsync<BasketDto>("/api/basket"))!.Items);
    }

    [Fact]
    public async Task Requests_without_a_customer_identity_are_refused()
    {
        using var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/basket")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/basket/checkout", null)).StatusCode);
    }

    [Fact]
    public async Task Invalid_quantities_are_unprocessable()
    {
        using var client = _factory.CreateCustomerClient("api-customer-4");

        var response = await client.PutAsJsonAsync($"/api/basket/items/{Guid.NewGuid()}", new { quantity = 1000 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_answers_202_with_the_order_location()
    {
        using var client = _factory.CreateCustomerClient("api-customer-5");
        var keyboard = _factory.Pricing.Add("Keyboard", 89.00m);
        await client.PutAsJsonAsync($"/api/basket/items/{keyboard.Id}", new { quantity = 1 });

        var response = await client.PostAsync("/api/basket/checkout", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<CheckoutAccepted>();
        Assert.Equal($"/api/orders/{accepted!.OrderId}", response.Headers.Location!.ToString());
        Assert.Empty((await client.GetFromJsonAsync<BasketDto>("/api/basket"))!.Items);
    }

    [Fact]
    public async Task Checking_out_an_empty_basket_is_unprocessable()
    {
        using var client = _factory.CreateCustomerClient("api-customer-6");

        var response = await client.PostAsync("/api/basket/checkout", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
