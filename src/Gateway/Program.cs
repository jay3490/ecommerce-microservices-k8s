using BuildingBlocks.Http;
using BuildingBlocks.Observability;
using Gateway.Auth;
using Gateway.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using StackExchange.Redis;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("gateway");

builder.Services.AddSingleton(TimeProvider.System);

// Shared state between gateway replicas (rate limit counters) lives in Redis when configured.
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var options = ConfigurationOptions.Parse(redisConnectionString);
        options.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(options);
    });
}

builder.Services.AddGatewayAuthentication(builder.Configuration);
builder.Services.AddGatewayRateLimiting(builder.Configuration);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context => context.AddRequestTransform(transform =>
    {
        // Services trust this header, so never forward a client-supplied value; set it only
        // from the token the gateway has just validated.
        transform.ProxyRequest.Headers.Remove(KnownHeaders.CustomerId);
        var subject = transform.HttpContext.User.FindFirst(AuthPolicies.SubjectClaim)?.Value;
        if (subject is not null)
        {
            transform.ProxyRequest.Headers.TryAddWithoutValidation(KnownHeaders.CustomerId, subject);
        }

        return ValueTask.CompletedTask;
    }));

// Behind an ingress controller the client address arrives in X-Forwarded-For.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    if (builder.Configuration.GetValue("Gateway:TrustAllProxies", false))
    {
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapDevTokenEndpoint();
app.MapReverseProxy();

await app.RunAsync();

public partial class Program;
