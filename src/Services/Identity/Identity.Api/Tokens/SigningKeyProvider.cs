using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Tokens;

/// <summary>
/// Holds the RSA key that signs access tokens and exposes its public half as a JSON Web Key,
/// which the gateway downloads through the discovery document to validate tokens.
/// </summary>
public sealed class SigningKeyProvider : IDisposable
{
    private readonly RSA _rsa;

    public SigningKeyProvider(IOptions<IdentityTokenOptions> options, ILogger<SigningKeyProvider> logger)
    {
        var settings = options.Value;

        if (!string.IsNullOrWhiteSpace(settings.SigningKeyPem))
        {
            _rsa = RSA.Create();
            _rsa.ImportFromPem(settings.SigningKeyPem);
        }
        else if (settings.AllowEphemeralSigningKey)
        {
            _rsa = RSA.Create(2048);
            logger.LogWarning("No signing key configured; using an ephemeral key. Tokens will not survive a restart.");
        }
        else
        {
            throw new InvalidOperationException(
                "Configure Identity:SigningKeyPem, or set Identity:AllowEphemeralSigningKey for local use.");
        }

        if (_rsa.KeySize < 2048)
        {
            throw new InvalidOperationException("The signing key must be an RSA key of at least 2048 bits.");
        }

        Key = new RsaSecurityKey(_rsa) { KeyId = settings.SigningKeyId };
        Credentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256);
    }

    public RsaSecurityKey Key { get; }

    public SigningCredentials Credentials { get; }

    /// <summary>The public key only, in the shape published at <c>/.well-known/jwks.json</c>.</summary>
    public JsonWebKey PublicJwk()
    {
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);
        return new JsonWebKey
        {
            Kty = JsonWebAlgorithmsKeyTypes.RSA,
            Use = JsonWebKeyUseNames.Sig,
            Alg = SecurityAlgorithms.RsaSha256,
            Kid = Key.KeyId,
            N = Base64UrlEncoder.Encode(parameters.Modulus),
            E = Base64UrlEncoder.Encode(parameters.Exponent),
        };
    }

    public void Dispose() => _rsa.Dispose();
}
