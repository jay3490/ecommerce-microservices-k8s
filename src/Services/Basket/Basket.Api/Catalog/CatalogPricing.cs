using BuildingBlocks.Observability;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Basket.Api.Catalog;

public sealed record PricedProduct(Guid Id, string Sku, string Name, decimal Price, string Currency);

/// <summary>Prices products at checkout. The catalog is the only source of prices.</summary>
public interface IProductPricing
{
    Task<IReadOnlyList<PricedProduct>> GetProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

public sealed class CatalogPricingClient(HttpClient http) : IProductPricing
{
    public async Task<IReadOnlyList<PricedProduct>> GetProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var query = string.Join("&", ids.Select(id => $"ids={id}"));
        var products = await http.GetFromJsonAsync<List<PricedProduct>>($"api/products/lookup?{query}", cancellationToken);
        return products ?? [];
    }

    /// <summary>Errors that mean the catalog could not be reached in time, after retries.</summary>
    public static bool IsUnavailable(Exception exception) =>
        exception is HttpRequestException or TimeoutRejectedException or BrokenCircuitException;
}

public static class CatalogPricingExtensions
{
    public static IServiceCollection AddCatalogPricing(this IServiceCollection services, IConfiguration configuration)
    {
        var baseAddress = configuration["Services:Catalog"]
                          ?? throw new InvalidOperationException("Services:Catalog is not configured.");

        services.AddHttpClient<IProductPricing, CatalogPricingClient>(client =>
                client.BaseAddress = new Uri(baseAddress.TrimEnd('/') + "/"))
            .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.Retry.MaxRetryAttempts = 3;
            });

        return services;
    }
}
