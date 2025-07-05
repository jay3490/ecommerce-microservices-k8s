using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gateway.Tests;

/// <summary>
/// Runs the real gateway in memory. Downstream clusters point at ports nobody listens on,
/// so a request that passes authentication and rate limiting comes back as 502 from YARP.
/// </summary>
public sealed class GatewayFactory : WebApplicationFactory<Program>
{
    public const int PermitLimit = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-with-at-least-32-characters");
        builder.UseSetting("Jwt:EnableDevTokenEndpoint", "true");
        builder.UseSetting("RateLimiting:PermitLimit", PermitLimit.ToString());
        builder.UseSetting("RateLimiting:Window", "00:01:00");
        foreach (var cluster in new[] { "catalog", "ordering", "inventory", "payment" })
        {
            builder.UseSetting($"ReverseProxy:Clusters:{cluster}:Destinations:primary:Address", "http://127.0.0.1:9");
        }
    }

    public async Task<HttpClient> CreateClientWithTokenAsync(string subject, params string[] roles)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/auth/dev-token", new { subject, roles });
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    private sealed record TokenResponse(string AccessToken);
}
