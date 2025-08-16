using BuildingBlocks.Health;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using BuildingBlocks.Outbox;
using BuildingBlocks.Persistence;
using Identity.Api.Accounts;
using Identity.Api.Data;
using Identity.Api.Domain;
using Identity.Api.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("identity-api");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<AppIdentityDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("IdentityDb")));

builder.Services
    .AddIdentityCore<ApplicationUser>(identity =>
    {
        identity.User.RequireUniqueEmail = true;
        identity.Password.RequiredLength = 10;
        identity.Password.RequireNonAlphanumeric = false;
        identity.Lockout.MaxFailedAccessAttempts = 5;
        identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppIdentityDbContext>();

builder.Services.AddOptions<IdentityTokenOptions>()
    .Bind(builder.Configuration.GetSection(IdentityTokenOptions.SectionName))
    .Validate(o => o.AccessTokenLifetime > TimeSpan.Zero && o.RefreshTokenLifetime > o.AccessTokenLifetime,
        "Token lifetimes must be positive and refresh tokens must outlive access tokens.")
    .ValidateOnStart();
builder.Services.AddSingleton<SigningKeyProvider>();
builder.Services.AddSingleton<AccessTokenIssuer>();
builder.Services.AddScoped<RefreshTokenService>();

// Identity only publishes (UserRegistered); it consumes nothing.
builder.Services.AddMessaging(builder.Configuration, queueName: "identity")
    .AddEntityFrameworkMessageStore<AppIdentityDbContext>();

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppIdentityDbContext>("postgres", tags: [HealthTags.Ready]);

var app = builder.Build();

// Fail at startup, not on the first login, when no signing key is available.
_ = app.Services.GetRequiredService<SigningKeyProvider>();

app.UseExceptionHandler();
app.UseServiceDefaults();
app.MapDiscoveryEndpoints();
app.MapAccountEndpoints();
app.MapOutboxAdminEndpoints<AppIdentityDbContext>();

await app.MigrateDatabaseAsync<AppIdentityDbContext>();
await app.SeedIdentityAsync();
await app.RunAsync();

public partial class Program;
