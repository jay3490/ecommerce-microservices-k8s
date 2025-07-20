using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Tests;

/// <summary>
/// The gateway configured with an authority, as it is when Identity.Api issues the tokens.
/// The discovery document is served from memory: its issuer and RSA key stand in for what
/// the gateway would download from <c>/.well-known/openid-configuration</c>.
/// </summary>
public sealed class AuthorityGatewayFactory : GatewayFactory
{
    public const string Issuer = "https://identity.test";

    public RsaSecurityKey IdentityKey { get; } = new(RSA.Create(2048)) { KeyId = "identity-1" };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Jwt:Authority", Issuer);

        builder.ConfigureServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(IdentityKey);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            }));
    }

    public static string CreateToken(SecurityKey key, string subject, string issuer = Issuer, params string[] roles)
    {
        var identity = new ClaimsIdentity([new Claim("sub", subject)]);
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim("role", role));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = identity,
            Issuer = issuer,
            Audience = "ecommerce-api",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });
    }
}

public sealed class IdentityTokenTests : IDisposable
{
    private readonly AuthorityGatewayFactory _factory = new();

    [Fact]
    public async Task Tokens_signed_by_the_identity_service_are_accepted()
    {
        using var client = ClientWith(AuthorityGatewayFactory.CreateToken(_factory.IdentityKey, "user-1", roles: "customer"));

        var response = await client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Roles_from_identity_tokens_drive_authorization()
    {
        using var customer = ClientWith(AuthorityGatewayFactory.CreateToken(_factory.IdentityKey, "user-1", roles: "customer"));
        using var admin = ClientWith(AuthorityGatewayFactory.CreateToken(_factory.IdentityKey, "ops-1", roles: "admin"));

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/inventory/x")).StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, (await admin.GetAsync("/api/inventory/x")).StatusCode);
    }

    [Fact]
    public async Task Tokens_signed_with_an_unknown_key_are_rejected()
    {
        using var otherKey = RSA.Create(2048);
        using var client = ClientWith(AuthorityGatewayFactory.CreateToken(new RsaSecurityKey(otherKey), "mallory", roles: "admin"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task Tokens_from_another_issuer_are_rejected_even_with_the_right_key()
    {
        using var client = ClientWith(AuthorityGatewayFactory.CreateToken(_factory.IdentityKey, "user-1", issuer: "https://elsewhere.test"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task Dev_tokens_keep_working_next_to_the_authority()
    {
        using var client = await _factory.CreateClientWithTokenAsync("customer-42", "customer");

        Assert.Equal(HttpStatusCode.BadGateway, (await client.GetAsync("/api/orders")).StatusCode);
    }

    [Fact]
    public async Task Registration_and_token_requests_are_public()
    {
        using var client = _factory.CreateClient();

        var register = await client.PostAsync("/api/identity/register", new StringContent("{}"));
        var me = await client.GetAsync("/api/identity/me");

        Assert.Equal(HttpStatusCode.BadGateway, register.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }
}
