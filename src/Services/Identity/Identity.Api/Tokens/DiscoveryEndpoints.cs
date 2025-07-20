using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Tokens;

/// <summary>
/// The minimal OpenID Connect discovery surface a JWT bearer handler needs: the issuer and
/// where to fetch the signing keys. The gateway points <c>Jwt:Authority</c> here.
/// </summary>
public static class DiscoveryEndpoints
{
    public const string ConfigurationPath = "/.well-known/openid-configuration";
    public const string JwksPath = "/.well-known/jwks.json";

    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ConfigurationPath, (HttpRequest request, IOptions<IdentityTokenOptions> options) =>
            Results.Ok(new Dictionary<string, object>
            {
                ["issuer"] = options.Value.Issuer,
                ["jwks_uri"] = $"{request.Scheme}://{request.Host}{request.PathBase}{JwksPath}",
                ["token_endpoint"] = $"{request.Scheme}://{request.Host}{request.PathBase}/api/identity/token",
                ["id_token_signing_alg_values_supported"] = new[] { SecurityAlgorithms.RsaSha256 },
                ["claims_supported"] = new[] { "sub", "email", "name", "role" },
            })).ExcludeFromDescription();

        endpoints.MapGet(JwksPath, (SigningKeyProvider keys) =>
        {
            var jwk = keys.PublicJwk();
            return Results.Ok(new
            {
                keys = new[]
                {
                    new { kty = jwk.Kty, use = jwk.Use, alg = jwk.Alg, kid = jwk.Kid, n = jwk.N, e = jwk.E },
                },
            });
        }).ExcludeFromDescription();

        return endpoints;
    }
}
