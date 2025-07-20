namespace Identity.Api.Domain;

/// <summary>
/// A refresh token as stored: only a SHA-256 hash of the value handed to the client is kept.
/// Tokens are single use. Each refresh revokes the presented token and issues its successor in
/// the same <see cref="FamilyId"/>; presenting a token that was already used revokes the whole
/// family, because it means a copy of the token is in someone else's hands.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public Guid FamilyId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevocationReason { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public static RefreshToken Create(string userId, string tokenHash, Guid familyId, DateTimeOffset now, TimeSpan lifetime) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = tokenHash,
        FamilyId = familyId,
        CreatedAt = now,
        ExpiresAt = now + lifetime,
    };

    public void Revoke(string reason, DateTimeOffset now, Guid? replacedBy = null)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevocationReason = reason;
        ReplacedById = replacedBy;
    }
}
