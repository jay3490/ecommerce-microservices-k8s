using BuildingBlocks.Http;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Outbox;
using Identity.Api.Data;
using Identity.Api.Domain;
using Identity.Api.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Identity.Api.Accounts;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var identity = endpoints.MapGroup("/api/identity").WithTags("Identity");

        identity.MapPost("/register", Register);
        identity.MapPost("/token", Login);
        identity.MapPost("/token/refresh", Refresh);
        identity.MapPost("/token/revoke", Revoke);
        identity.MapGet("/me", Me);
        identity.MapPut("/users/{id}/roles", SetRoles);

        return endpoints;
    }

    /// <summary>
    /// Creates a customer account and signs it in. The user, its role, the first refresh
    /// token and the <see cref="UserRegistered"/> message commit in one transaction.
    /// </summary>
    private static async Task<IResult> Register(
        RegisterRequest request,
        UserManager<ApplicationUser> users,
        AppIdentityDbContext db,
        IOutbox outbox,
        AccessTokenIssuer accessTokens,
        RefreshTokenService refreshTokens,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var email = request.Email!.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName!.Trim(),
            CreatedAt = clock.GetUtcNow(),
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var created = await users.CreateAsync(user, request.Password!);
        if (!created.Succeeded)
        {
            return IdentityProblem(created);
        }

        var roleAdded = await users.AddToRoleAsync(user, Roles.Customer);
        if (!roleAdded.Succeeded)
        {
            return IdentityProblem(roleAdded);
        }

        outbox.Enqueue(new UserRegistered(user.Id, email, user.DisplayName));
        var refreshToken = refreshTokens.Issue(user.Id);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        IReadOnlyList<string> roles = [Roles.Customer];
        var accessToken = accessTokens.Issue(user, roles);
        return Results.Created("/api/identity/me", Tokens(accessToken, refreshToken, roles));
    }

    /// <summary>Password login. Repeated failures lock the account for a while.</summary>
    private static async Task<IResult> Login(
        LoginRequest request,
        UserManager<ApplicationUser> users,
        AppIdentityDbContext db,
        AccessTokenIssuer accessTokens,
        RefreshTokenService refreshTokens,
        CancellationToken cancellationToken)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || string.IsNullOrEmpty(request.Password))
        {
            return InvalidCredentials();
        }

        if (await users.IsLockedOutAsync(user))
        {
            return Results.Problem(statusCode: StatusCodes.Status423Locked, title: "The account is temporarily locked.");
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            return InvalidCredentials();
        }

        await users.ResetAccessFailedCountAsync(user);
        var roles = (await users.GetRolesAsync(user)).Order(StringComparer.Ordinal).ToList();
        var refreshToken = refreshTokens.Issue(user.Id);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(Tokens(accessTokens.Issue(user, roles), refreshToken, roles));
    }

    /// <summary>
    /// Exchanges a refresh token for a new access token and a new refresh token. Roles are
    /// read again, so a role change takes effect at the next refresh.
    /// </summary>
    private static async Task<IResult> Refresh(
        RefreshRequest request,
        UserManager<ApplicationUser> users,
        AccessTokenIssuer accessTokens,
        RefreshTokenService refreshTokens,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var result = await refreshTokens.RotateAsync(request.RefreshToken, cancellationToken);
        if (result.Outcome == RefreshOutcome.Reused)
        {
            loggerFactory.CreateLogger(typeof(AccountEndpoints))
                .LogWarning("Refresh token reuse for user {UserId}; the session was revoked", result.UserId);
        }

        if (!result.Succeeded)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The refresh token is not valid.");
        }

        var user = await users.FindByIdAsync(result.UserId!);
        if (user is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The refresh token is not valid.");
        }

        var roles = (await users.GetRolesAsync(user)).Order(StringComparer.Ordinal).ToList();
        return Results.Ok(Tokens(accessTokens.Issue(user, roles), result.NewToken!, roles));
    }

    private static async Task<IResult> Revoke(
        RefreshRequest request, RefreshTokenService refreshTokens, CancellationToken cancellationToken)
    {
        await refreshTokens.RevokeAsync(request.RefreshToken, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> Me(
        [FromHeader(Name = KnownHeaders.CustomerId)] string? customerId,
        UserManager<ApplicationUser> users)
    {
        var user = string.IsNullOrWhiteSpace(customerId) ? null : await users.FindByIdAsync(customerId);
        return user is null ? Results.NotFound() : Results.Ok(await ToDtoAsync(user, users));
    }

    /// <summary>
    /// Replaces a user's roles. The gateway only lets admins reach this endpoint. Existing
    /// sessions are revoked so the new roles are picked up at the next login.
    /// </summary>
    private static async Task<IResult> SetRoles(
        string id,
        SetRolesRequest request,
        UserManager<ApplicationUser> users,
        AppIdentityDbContext db,
        RefreshTokenService refreshTokens,
        CancellationToken cancellationToken)
    {
        var requested = request.Roles?.Distinct(StringComparer.Ordinal).ToList() ?? [];
        if (requested.Count == 0 || requested.Except(Roles.All).Any())
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Roles"] = [$"Give one or more of: {string.Join(", ", Roles.All)}."],
            });
        }

        var user = await users.FindByIdAsync(id);
        if (user is null)
        {
            return Results.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var current = await users.GetRolesAsync(user);
        var removed = await users.RemoveFromRolesAsync(user, current.Except(requested));
        if (!removed.Succeeded)
        {
            return IdentityProblem(removed);
        }

        var added = await users.AddToRolesAsync(user, requested.Except(current));
        if (!added.Succeeded)
        {
            return IdentityProblem(added);
        }

        await refreshTokens.RevokeAllForUserAsync(user.Id, "Roles changed", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(await ToDtoAsync(user, users));
    }

    private static TokenResponse Tokens(AccessToken access, string refreshToken, IReadOnlyList<string> roles) =>
        new(access.Value, "Bearer", access.ExpiresAt, refreshToken, roles);

    private static async Task<UserDto> ToDtoAsync(ApplicationUser user, UserManager<ApplicationUser> users)
    {
        var roles = (await users.GetRolesAsync(user)).Order(StringComparer.Ordinal).ToList();
        return new UserDto(user.Id, user.Email ?? string.Empty, user.DisplayName, roles, user.CreatedAt);
    }

    private static IResult InvalidCredentials() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");

    private static IResult IdentityProblem(IdentityResult result)
    {
        var duplicate = result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName));
        if (duplicate)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "An account with this email already exists.");
        }

        return Results.ValidationProblem(result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
    }
}
