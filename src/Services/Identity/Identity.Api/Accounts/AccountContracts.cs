using Identity.Api.Domain;

namespace Identity.Api.Accounts;

public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Email) || Email.Length > 256 || !Email.Contains('@'))
        {
            errors[nameof(Email)] = ["A valid email address of at most 256 characters is required."];
        }

        if (string.IsNullOrEmpty(Password))
        {
            errors[nameof(Password)] = ["A password is required."];
        }

        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Trim().Length > ApplicationUser.MaxDisplayNameLength)
        {
            errors[nameof(DisplayName)] = [$"A display name of at most {ApplicationUser.MaxDisplayNameLength} characters is required."];
        }

        return errors;
    }
}

public sealed record LoginRequest(string? Email, string? Password);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record SetRolesRequest(IReadOnlyList<string>? Roles);

/// <summary>What a successful login, registration or refresh returns.</summary>
public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    IReadOnlyList<string> Roles);

public sealed record UserDto(string Id, string Email, string DisplayName, IReadOnlyList<string> Roles, DateTimeOffset CreatedAt);
