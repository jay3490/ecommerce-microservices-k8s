using BuildingBlocks.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace BuildingBlocks.Observability;

public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Registers the cross-cutting pieces every service uses: Serilog, OpenTelemetry traces and
    /// metrics, and the liveness health check. Readiness checks are added by each service.
    /// </summary>
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        builder.AddSerilog(serviceName);
        builder.AddOpenTelemetry(serviceName);

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [HealthTags.Live]);

        builder.Services.AddTransient<CorrelationIdDelegatingHandler>();

        return builder;
    }

    /// <summary>
    /// Adds correlation id handling and request logging. Call before mapping endpoints.
    /// </summary>
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (context, _, exception) =>
                exception is not null || context.Response.StatusCode >= 500
                    ? Serilog.Events.LogEventLevel.Error
                    : IsHealthRequest(context)
                        ? Serilog.Events.LogEventLevel.Verbose
                        : Serilog.Events.LogEventLevel.Information;
        });

        app.MapHealthEndpoints();
        return app;
    }

    private static void AddSerilog(this WebApplicationBuilder builder, string serviceName)
    {
        var jsonLogs = builder.Configuration.GetValue("Observability:JsonLogs", false);

        builder.Host.UseSerilog((context, services, logger) =>
        {
            logger
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", serviceName);

            if (jsonLogs)
            {
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
            else
            {
                logger.WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}");
            }
        });
    }

    private static void AddOpenTelemetry(this WebApplicationBuilder builder, string serviceName)
    {
        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName,
                serviceVersion: typeof(ServiceDefaultsExtensions).Assembly.GetName().Version?.ToString()))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !IsHealthRequest(context))
                .AddHttpClientInstrumentation()
                .AddSource(Telemetry.Name))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(Telemetry.Name));

        // Export only when a collector is configured, so running locally without one stays quiet.
        var endpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            var protocol = builder.Configuration.GetValue("OpenTelemetry:Protocol", OtlpExportProtocol.Grpc);
            otel.UseOtlpExporter(protocol, new Uri(endpoint));
        }
    }

    private static bool IsHealthRequest(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/health");
}
