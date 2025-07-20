using System.Security.Claims;
using Identity.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Tokens;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// Creates RS256 access tokens. Claim names match what the gateway reads: <c>sub</c> for the
/// customer id and one <c>role</c> claim per role.
/// </summary>
public sealed class AccessTokenIssuer(SigningKeyProvider keys, IOptions<IdentityTokenOptions> options, TimeProvider clock)
{
    public const string SubjectClaim = "sub";
    public const string RoleClaim = "role";

    public AccessToken Issue(ApplicationUser user, IEnumerable<string> roles)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var expires = now + settings.AccessTokenLifetime;

        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(SubjectClaim, user.Id));
        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")));
        if (user.Email is not null)
        {
            identity.AddClaim(new Claim(JwtRegisteredClaimNames.Email, user.Email));
        }

        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Name, user.DisplayName));
        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            identity.AddClaim(new Claim(RoleClaim, role));
        }

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = identity,
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = keys.Credentials,
        });

        return new AccessToken(token, expires);
    }
}
