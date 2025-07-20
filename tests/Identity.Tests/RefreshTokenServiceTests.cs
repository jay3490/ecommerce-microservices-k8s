using Identity.Api.Data;
using Identity.Api.Domain;
using Identity.Api.Tokens;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Identity.Tests;

public sealed class RefreshTokenServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly IOptions<IdentityTokenOptions> _options = Options.Create(new IdentityTokenOptions
    {
        RefreshTokenLifetime = TimeSpan.FromHours(1),
    });

    public RefreshTokenServiceTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
        db.Users.Add(new ApplicationUser { Id = "user-1", UserName = "u1", Email = "u1@example.com", DisplayName = "U1" });
        db.SaveChanges();
    }

    [Fact]
    public async Task Only_a_hash_of_the_token_is_stored()
    {
        string token;
        await using (var db = CreateDb())
        {
            token = new RefreshTokenService(db, _options, _clock).Issue("user-1");
            await db.SaveChangesAsync();
        }

        await using var check = CreateDb();
        var stored = await check.RefreshTokens.SingleAsync();
        Assert.NotEqual(token, stored.TokenHash);
        Assert.Equal(RefreshTokenService.Hash(token), stored.TokenHash);
        Assert.Equal(_clock.Now.AddHours(1), stored.ExpiresAt);
    }

    [Fact]
    public async Task Expired_tokens_cannot_be_rotated()
    {
        var token = await IssueAsync();
        _clock.Now = _clock.Now.AddHours(1).AddSeconds(1);

        await using var db = CreateDb();
        var result = await new RefreshTokenService(db, _options, _clock).RotateAsync(token, CancellationToken.None);

        Assert.Equal(RefreshOutcome.Expired, result.Outcome);
        Assert.Null(result.NewToken);
    }

    [Fact]
    public async Task Rotation_keeps_the_family_and_links_the_successor()
    {
        var token = await IssueAsync();

        RefreshResult result;
        await using (var db = CreateDb())
        {
            result = await new RefreshTokenService(db, _options, _clock).RotateAsync(token, CancellationToken.None);
        }

        Assert.Equal(RefreshOutcome.Rotated, result.Outcome);
        Assert.Equal("user-1", result.UserId);

        await using var check = CreateDb();
        var tokens = await check.RefreshTokens.ToListAsync();
        var old = tokens.Single(t => t.TokenHash == RefreshTokenService.Hash(token));
        var successor = tokens.Single(t => t.TokenHash == RefreshTokenService.Hash(result.NewToken!));
        Assert.Equal(old.FamilyId, successor.FamilyId);
        Assert.Equal(successor.Id, old.ReplacedById);
        Assert.False(old.IsActive(_clock.Now));
        Assert.True(successor.IsActive(_clock.Now));
    }

    [Fact]
    public async Task A_reused_token_revokes_every_token_in_its_family()
    {
        var token = await IssueAsync();
        await using (var db = CreateDb())
        {
            await new RefreshTokenService(db, _options, _clock).RotateAsync(token, CancellationToken.None);
        }

        await using (var db = CreateDb())
        {
            var replay = await new RefreshTokenService(db, _options, _clock).RotateAsync(token, CancellationToken.None);
            Assert.Equal(RefreshOutcome.Reused, replay.Outcome);
        }

        await using var check = CreateDb();
        Assert.All(await check.RefreshTokens.ToListAsync(), t => Assert.False(t.IsActive(_clock.Now)));
    }

    public void Dispose() => _connection.Dispose();

    private async Task<string> IssueAsync()
    {
        await using var db = CreateDb();
        var token = new RefreshTokenService(db, _options, _clock).Issue("user-1");
        await db.SaveChangesAsync();
        return token;
    }

    private AppIdentityDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppIdentityDbContext>().UseSqlite(_connection).Options);

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
