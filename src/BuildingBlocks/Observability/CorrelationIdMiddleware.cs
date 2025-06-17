using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace BuildingBlocks.Observability;

/// <summary>
/// Reads the correlation id from the incoming request (or creates one), writes it back on
/// the request so a reverse proxy forwards it, echoes it on the response and adds it to the
/// log context and the current trace.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[CorrelationContext.HeaderName].ToString();
        var correlationId = CorrelationContext.IsValid(incoming) ? incoming : CorrelationContext.NewId();

        context.Request.Headers[CorrelationContext.HeaderName] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationContext.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        CorrelationContext.Current = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);

        using (LogContext.PushProperty(CorrelationContext.LogPropertyName, correlationId))
        {
            await next(context);
        }
    }
}
