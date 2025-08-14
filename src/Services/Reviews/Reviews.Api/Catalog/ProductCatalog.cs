using BuildingBlocks.Observability;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Reviews.Api.Catalog;

/// <summary>Checks that a product exists before it can be reviewed.</summary>
public interface IProductCatalog
{
    Task<bool> ExistsAsync(Guid productId, CancellationToken cancellationToken);
}

public sealed class CatalogClient(HttpClient http) : IProductCatalog
{
    public async Task<bool> ExistsAsync(Guid productId, CancellationToken cancellationToken)
    {
        var products = await http.GetFromJsonAsync<List<ProductId>>($"api/products/lookup?ids={productId}", cancellationToken);
        return products is { Count: > 0 };
    }

    public static bool IsUnavailable(Exception exception) =>
        exception is HttpRequestException or TimeoutRejectedException or BrokenCircuitException;

    private sealed record ProductId(Guid Id);
}

public static class ProductCatalogExtensions
{
    public static IServiceCollection AddProductCatalog(this IServiceCollection services, IConfiguration configuration)
    {
        var baseAddress = configuration["Services:Catalog"]
                          ?? throw new InvalidOperationException("Services:Catalog is not configured.");

        services.AddHttpClient<IProductCatalog, CatalogClient>(client => client.BaseAddress = new Uri(baseAddress.TrimEnd('/') + "/"))
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
            });

        return services;
    }
}
