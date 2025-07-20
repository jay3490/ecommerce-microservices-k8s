namespace Identity.Api.Tokens;

public sealed class IdentityTokenOptions
{
    public const string SectionName = "Identity";

    /// <summary>Written to the <c>iss</c> claim and published in the discovery document.</summary>
    public string Issuer { get; set; } = "ecommerce-identity";

    public string Audience { get; set; } = "ecommerce-api";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>PKCS#8 or PKCS#1 PEM of the RSA private key that signs access tokens.</summary>
    public string? SigningKeyPem { get; set; }

    /// <summary>The <c>kid</c> published in the JWKS; change it when rotating the key.</summary>
    public string SigningKeyId { get; set; } = "identity-1";

    /// <summary>
    /// Without <see cref="SigningKeyPem"/>, generate a key at startup. Tokens then stop
    /// validating when the process restarts and differ between replicas, so this is only for
    /// local and single-replica dev environments.
    /// </summary>
    public bool AllowEphemeralSigningKey { get; set; }

    public SeedAdminOptions SeedAdmin { get; set; } = new();
}

/// <summary>An administrator account created at startup if it does not exist yet.</summary>
public sealed class SeedAdminOptions
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}
