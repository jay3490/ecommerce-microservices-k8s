using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy;

namespace Gateway.Tests;

/// <summary>Checks the route table the gateway actually loads from appsettings.json.</summary>
public sealed class RoutingConfigTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    public static TheoryData<string, string, string, string[]?> ExpectedRoutes => new()
    {
        { "catalog-read", "catalog", "anonymous", ["GET"] },
        { "catalog-write", "catalog", "admin", ["POST", "PUT"] },
        { "orders", "ordering", "customer", null },
        { "inventory", "inventory", "admin", null },
        { "payments", "payment", "admin", null },
    };

    [Theory]
    [MemberData(nameof(ExpectedRoutes))]
    public void Each_route_targets_the_right_cluster_with_its_policies(
        string routeId, string clusterId, string authorizationPolicy, string[]? methods)
    {
        var route = Proxy().GetRoutes().Single(r => r.Config.RouteId == routeId).Config;

        Assert.Equal(clusterId, route.ClusterId);
        Assert.Equal(authorizationPolicy, route.AuthorizationPolicy);
        Assert.Equal("per-client", route.RateLimiterPolicy);
        Assert.Equal(methods, route.Match.Methods?.ToArray());
    }

    [Fact]
    public void Every_route_points_at_a_cluster_with_a_destination()
    {
        var proxy = Proxy();
        var routes = proxy.GetRoutes().ToList();

        Assert.Equal(ExpectedRoutes.Count, routes.Count);
        foreach (var route in routes)
        {
            Assert.True(proxy.TryGetCluster(route.Config.ClusterId!, out var cluster), $"Missing cluster for {route.Config.RouteId}");
            Assert.NotEmpty(cluster.Destinations);
        }
    }

    [Fact]
    public void Routes_are_not_left_unprotected_by_accident()
    {
        // Only catalog reads are public; everything else needs a policy.
        var anonymous = Proxy().GetRoutes()
            .Where(r => string.Equals(r.Config.AuthorizationPolicy, "anonymous", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Config.RouteId);

        Assert.Equal(["catalog-read"], anonymous);
    }

    [Fact]
    public void Shipped_configuration_uses_production_safe_defaults()
    {
        var shipped = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .Build();

        Assert.False(shipped.GetValue<bool>("Jwt:EnableDevTokenEndpoint"));
        Assert.Null(shipped["Jwt:SigningKey"]);
    }

    private IProxyStateLookup Proxy()
    {
        // Starting the host loads and validates the YARP configuration.
        _ = factory.Server;
        return factory.Services.GetRequiredService<IProxyStateLookup>();
    }
}
