using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// OpenID Connect authority (for example an Entra ID or Keycloak realm URL). When set,
    /// signing keys and the issuer come from its discovery document and
    /// <see cref="SigningKey"/> is ignored.
    /// </summary>
    public string? Authority { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Issuer expected (and, for dev tokens, written) when no authority is used.</summary>
    public string Issuer { get; set; } = "ecommerce-gateway";

    public string Audience { get; set; } = "ecommerce-api";

    /// <summary>HMAC key for locally issued tokens; at least 32 characters.</summary>
    public string? SigningKey { get; set; }

    /// <summary>Exposes POST /auth/dev-token. Only for local and dev environments.</summary>
    public bool EnableDevTokenEndpoint { get; set; }

    public TimeSpan DevTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    public bool UsesAuthority => !string.IsNullOrWhiteSpace(Authority);

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey!));
}

public static class AuthPolicies
{
    public const string Customer = "customer";
    public const string Admin = "admin";

    public const string SubjectClaim = "sub";
    public const string RoleClaim = "role";
}

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddGatewayAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                jwt => jwt.UsesAuthority || jwt.SigningKey is { Length: >= 32 },
                "Configure Jwt:Authority, or Jwt:SigningKey with at least 32 characters.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, options) =>
            {
                var jwt = options.Value;

                // Keep claim names as they appear in the token ("sub", "role").
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AuthPolicies.SubjectClaim,
                    RoleClaimType = AuthPolicies.RoleClaim,
                };

                if (jwt.UsesAuthority)
                {
                    bearer.Authority = jwt.Authority;
                    bearer.RequireHttpsMetadata = jwt.RequireHttpsMetadata;
                }
                else
                {
                    bearer.TokenValidationParameters.ValidIssuer = jwt.Issuer;
                    bearer.TokenValidationParameters.IssuerSigningKey = jwt.CreateSigningKey();
                }
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Customer, policy => policy.RequireAuthenticatedUser().RequireClaim(AuthPolicies.SubjectClaim))
            .AddPolicy(AuthPolicies.Admin, policy => policy.RequireRole("admin"));

        return services;
    }
}
