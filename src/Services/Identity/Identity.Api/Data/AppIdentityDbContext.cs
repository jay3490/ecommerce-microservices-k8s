using BuildingBlocks.Persistence;
using Identity.Api.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Identity.Api.Data;

public sealed class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.DisplayName).HasMaxLength(ApplicationUser.MaxDisplayNameLength).IsRequired();
        });

        builder.Entity<RefreshToken>(token =>
        {
            token.ToTable("refresh_tokens");
            token.HasKey(t => t.Id);
            token.Property(t => t.UserId).HasMaxLength(450).IsRequired();
            token.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
            token.Property(t => t.RevocationReason).HasMaxLength(100);

            // Two concurrent refreshes with the same token cannot both revoke it and get a successor.
            token.Property(t => t.RevokedAt).IsConcurrencyToken();
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasIndex(t => t.FamilyId);
            token.HasIndex(t => t.UserId);
            token.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.AddMessageStore();
    }
}
