using BuildingBlocks.Observability;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Ordering.Api.Catalog;

public sealed record CatalogProduct(Guid Id, string Sku, string Name, decimal Price, string Currency);

/// <summary>Typed client for the catalog's product lookup, used to price new orders.</summary>
public sealed class CatalogClient(HttpClient http)
{
    public async Task<IReadOnlyList<CatalogProduct>> GetProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var query = string.Join("&", ids.Select(id => $"ids={id}"));
        var products = await http.GetFromJsonAsync<List<CatalogProduct>>($"api/products/lookup?{query}", cancellationToken);
        return products ?? [];
    }

    /// <summary>Errors that mean the catalog could not be reached in time, after retries.</summary>
    public static bool IsUnavailable(Exception exception) =>
        exception is HttpRequestException or TimeoutRejectedException or BrokenCircuitException;
}

public static class CatalogClientExtensions
{
    public static IServiceCollection AddCatalogClient(this IServiceCollection services, IConfiguration configuration)
    {
        var baseAddress = configuration["Services:Catalog"]
                          ?? throw new InvalidOperationException("Services:Catalog is not configured.");

        // Standard pipeline from Microsoft.Extensions.Http.Resilience (Polly): rate limiter,
        // total timeout, retry with jittered backoff, circuit breaker and per-attempt timeout.
        services.AddHttpClient<CatalogClient>(client => client.BaseAddress = new Uri(baseAddress.TrimEnd('/') + "/"))
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
