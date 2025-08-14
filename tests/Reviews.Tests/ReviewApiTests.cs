using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Reviews.Api.Catalog;
using Reviews.Api.Reviews;
using Reviews.Api.Storage;

namespace Reviews.Tests;

public sealed class ReviewsFactory : WebApplicationFactory<Program>
{
    public InMemoryReviewStore Store { get; } = new();
    public FakeCatalog Catalog { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IReviewStore>();
            services.AddSingleton<IReviewStore>(Store);
            services.RemoveAll<IProductCatalog>();
            services.AddSingleton<IProductCatalog>(Catalog);
        });
    }

    public HttpClient CustomerClient(string customerId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Customer-Id", customerId);
        return client;
    }
}

public sealed class FakeCatalog : IProductCatalog
{
    public HashSet<Guid> Products { get; } = [];

    public Task<bool> ExistsAsync(Guid productId, CancellationToken cancellationToken) =>
        Task.FromResult(Products.Contains(productId));

    public Guid Add()
    {
        var id = Guid.NewGuid();
        Products.Add(id);
        return id;
    }
}

public sealed class ReviewApiTests : IClassFixture<ReviewsFactory>
{
    private readonly ReviewsFactory _factory;

    public ReviewApiTests(ReviewsFactory factory) => _factory = factory;

    [Fact]
    public async Task Customers_review_a_product_and_everyone_can_read_the_reviews_and_summary()
    {
        var product = _factory.Catalog.Add();
        using var alice = _factory.CustomerClient("alice");
        using var bob = _factory.CustomerClient("bob");
        using var anonymous = _factory.CreateClient();

        var created = await alice.PutAsJsonAsync($"/api/reviews/products/{product}", new { rating = 5, title = "Great", authorName = "Alice" });
        await bob.PutAsJsonAsync($"/api/reviews/products/{product}", new { rating = 2, title = "Meh" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reviews = await anonymous.GetFromJsonAsync<List<ReviewDto>>($"/api/reviews/products/{product}");
        Assert.Equal(2, reviews!.Count);
        var summary = await anonymous.GetFromJsonAsync<RatingSummaryDto>($"/api/reviews/products/{product}/summary");
        Assert.Equal(2, summary!.Count);
        Assert.Equal(3.5m, summary.Average);
        Assert.Equal(1, summary.Stars[5]);
        Assert.Equal(1, summary.Stars[2]);
    }

    [Fact]
    public async Task A_second_review_by_the_same_customer_replaces_the_first()
    {
        var product = _factory.Catalog.Add();
        using var alice = _factory.CustomerClient("alice");

        await alice.PutAsJsonAsync($"/api/reviews/products/{product}", new { rating = 1, title = "Broken" });
        var updated = await alice.PutAsJsonAsync($"/api/reviews/products/{product}", new { rating = 4, title = "Replacement works" });

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var summary = await alice.GetFromJsonAsync<RatingSummaryDto>($"/api/reviews/products/{product}/summary");
        Assert.Equal((1, 4m), (summary!.Count, summary.Average));
    }

    [Fact]
    public async Task Deleting_a_review_takes_it_out_of_the_summary()
    {
        var product = _factory.Catalog.Add();
        using var alice = _factory.CustomerClient("alice");
        await alice.PutAsJsonAsync($"/api/reviews/products/{product}", new { rating = 3, title = "Ok" });

        var deleted = await alice.DeleteAsync($"/api/reviews/products/{product}");
        var again = await alice.DeleteAsync($"/api/reviews/products/{product}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(0, (await alice.GetFromJsonAsync<RatingSummaryDto>($"/api/reviews/products/{product}/summary"))!.Count);
    }

    [Fact]
    public async Task Unknown_products_cannot_be_reviewed_and_invalid_input_is_rejected()
    {
        using var alice = _factory.CustomerClient("alice");
        var product = _factory.Catalog.Add();

        var unknown = await alice.PutAsJsonAsync($"/api/reviews/products/{Guid.NewGuid()}", new { rating = 5, title = "?" });
        var invalid = await alice.PutAsJsonAsync($"/api/reviews/products/{product}", new { rating = 9, title = "" });

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Writing_needs_a_customer_identity()
    {
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PutAsJsonAsync($"/api/reviews/products/{_factory.Catalog.Add()}", new { rating = 5, title = "Hi" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_drifted_summary_can_be_recomputed_from_the_reviews()
    {
        var product = _factory.Catalog.Add();
        using var alice = _factory.CustomerClient("alice");
        await alice.PutAsJsonAsync($"/api/reviews/products/{product}", new { rating = 5, title = "Great" });
        _factory.Store.CorruptSummary(product);

        var response = await alice.PostAsync($"/api/reviews/products/{product}/summary/recompute", null);

        var summary = (await response.Content.ReadFromJsonAsync<RatingSummaryDto>())!;
        Assert.Equal((1, 5m), (summary.Count, summary.Average));
    }
}
