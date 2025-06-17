namespace BuildingBlocks.Observability;

/// <summary>
/// Adds the current correlation id to outgoing HTTP requests made through HttpClientFactory.
/// </summary>
public sealed class CorrelationIdDelegatingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = CorrelationContext.Current;
        if (correlationId is not null && !request.Headers.Contains(CorrelationContext.HeaderName))
        {
            request.Headers.Add(CorrelationContext.HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
