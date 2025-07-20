using Identity.Api.Domain;
using Identity.Api.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Identity.Api.Data;

/// <summary>Creates the roles and, when configured, an administrator account.</summary>
public static class IdentitySeeder
{
    public static async Task SeedIdentityAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var clock = services.GetRequiredService<TimeProvider>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));

        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole(role));
            }
        }

        var admin = services.GetRequiredService<IOptions<IdentityTokenOptions>>().Value.SeedAdmin;
        if (string.IsNullOrWhiteSpace(admin.Email) || string.IsNullOrEmpty(admin.Password)
            || await users.FindByEmailAsync(admin.Email) is not null)
        {
            return;
        }

        var user = new ApplicationUser
        {
            UserName = admin.Email,
            Email = admin.Email,
            DisplayName = "Administrator",
            CreatedAt = clock.GetUtcNow(),
        };

        var created = await users.CreateAsync(user, admin.Password);
        if (!created.Succeeded)
        {
            logger.LogError("Could not create the seed administrator: {Errors}",
                string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }

        await users.AddToRolesAsync(user, Roles.All);
        logger.LogInformation("Created the seed administrator {Email}", admin.Email);
    }
}
