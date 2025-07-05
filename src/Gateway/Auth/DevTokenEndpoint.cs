using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Auth;

public sealed record DevTokenRequest(string? Subject, string[]? Roles);

public sealed record DevTokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

/// <summary>
/// Issues HMAC-signed tokens so the system can be exercised without an identity provider.
/// It is mapped only when <see cref="JwtOptions.EnableDevTokenEndpoint"/> is on and no
/// external authority is configured.
/// </summary>
public static class DevTokenEndpoint
{
    private static readonly string[] AllowedRoles = ["customer", "admin"];

    public static IEndpointRouteBuilder MapDevTokenEndpoint(this IEndpointRouteBuilder endpoints)
    {
        var jwt = endpoints.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value;
        if (!jwt.EnableDevTokenEndpoint || jwt.UsesAuthority)
        {
            return endpoints;
        }

        endpoints.MapPost("/auth/dev-token", IssueToken).AllowAnonymous().WithTags("Auth");
        return endpoints;
    }

    private static IResult IssueToken(DevTokenRequest request, IOptions<JwtOptions> options, TimeProvider clock)
    {
        var subject = request.Subject?.Trim();
        if (string.IsNullOrEmpty(subject) || subject.Length > 100)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["subject"] = ["A subject of at most 100 characters is required."],
            });
        }

        var roles = request.Roles is { Length: > 0 } ? request.Roles : ["customer"];
        if (roles.Except(AllowedRoles).Any())
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["roles"] = [$"Roles must be among: {string.Join(", ", AllowedRoles)}."],
            });
        }

        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.Add(jwt.DevTokenLifetime);

        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(AuthPolicies.SubjectClaim, subject));
        foreach (var role in roles.Distinct())
        {
            identity.AddClaim(new Claim(AuthPolicies.RoleClaim, role));
        }

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = identity,
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(jwt.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        });

        return Results.Ok(new DevTokenResponse(token, "Bearer", expires));
    }
}
