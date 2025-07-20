using System.Security.Cryptography;
using System.Text;
using Identity.Api.Data;
using Identity.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Tokens;

public enum RefreshOutcome
{
    Rotated,
    Unknown,
    Expired,

    /// <summary>An already used token came back: the whole family has been revoked.</summary>
    Reused,
}

public sealed record RefreshResult(RefreshOutcome Outcome, string? UserId, string? NewToken)
{
    public bool Succeeded => Outcome == RefreshOutcome.Rotated;
}

/// <summary>
/// Issues, rotates and revokes refresh tokens. The client receives 32 random bytes encoded
/// as base64url; the database only ever sees their SHA-256 hash.
/// </summary>
public sealed class RefreshTokenService(AppIdentityDbContext db, IOptions<IdentityTokenOptions> options, TimeProvider clock)
{
    private const int MaxTokenLength = 256;

    /// <summary>Starts a new token family (a login). The caller saves the changes.</summary>
    public string Issue(string userId) => Add(userId, Guid.NewGuid(), out _);

    /// <summary>
    /// Exchanges a refresh token for its successor and saves. A token that was already used
    /// revokes every token in its family, logging out whoever holds the stolen copy as well.
    /// </summary>
    public async Task<RefreshResult> RotateAsync(string? presentedToken, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var stored = await FindAsync(presentedToken, cancellationToken);
        if (stored is null)
        {
            return new RefreshResult(RefreshOutcome.Unknown, null, null);
        }

        if (stored.RevokedAt is not null)
        {
            await RevokeFamilyAsync(stored.FamilyId, "Reuse detected", now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return new RefreshResult(RefreshOutcome.Reused, stored.UserId, null);
        }

        if (stored.ExpiresAt <= now)
        {
            return new RefreshResult(RefreshOutcome.Expired, stored.UserId, null);
        }

        var successor = Add(stored.UserId, stored.FamilyId, out var successorId);
        stored.Revoke("Rotated", now, successorId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request rotated the same token a moment earlier (RevokedAt is a
            // concurrency token), which is a reuse as well.
            db.ChangeTracker.Clear();
            var family = stored.FamilyId;
            await RevokeFamilyAsync(family, "Reuse detected", now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return new RefreshResult(RefreshOutcome.Reused, stored.UserId, null);
        }

        return new RefreshResult(RefreshOutcome.Rotated, stored.UserId, successor);
    }

    /// <summary>Logs out the session the token belongs to and saves. Unknown tokens are ignored.</summary>
    public async Task RevokeAsync(string? presentedToken, CancellationToken cancellationToken)
    {
        var stored = await FindAsync(presentedToken, cancellationToken);
        if (stored is null)
        {
            return;
        }

        await RevokeFamilyAsync(stored.FamilyId, "Logged out", clock.GetUtcNow(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Revokes every active token of a user, for example after a role change. The caller saves.</summary>
    public async Task RevokeAllForUserAsync(string userId, string reason, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in active)
        {
            token.Revoke(reason, now);
        }
    }

    internal static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private string Add(string userId, Guid familyId, out Guid id)
    {
        var value = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var token = RefreshToken.Create(userId, Hash(value), familyId, clock.GetUtcNow(), options.Value.RefreshTokenLifetime);
        db.RefreshTokens.Add(token);
        id = token.Id;
        return value;
    }

    private async Task<RefreshToken?> FindAsync(string? presentedToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(presentedToken) || presentedToken.Length > MaxTokenLength)
        {
            return null;
        }

        var hash = Hash(presentedToken);
        return await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
    }

    private async Task RevokeFamilyAsync(Guid familyId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var family = await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in family)
        {
            token.Revoke(reason, now);
        }
    }
}
