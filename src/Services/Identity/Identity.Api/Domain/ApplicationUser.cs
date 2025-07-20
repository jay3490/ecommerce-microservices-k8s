using Microsoft.AspNetCore.Identity;

namespace Identity.Api.Domain;

/// <summary>An account. Its <c>Id</c> becomes the <c>sub</c> claim and the customer id.</summary>
public sealed class ApplicationUser : IdentityUser
{
    public const int MaxDisplayNameLength = 100;

    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public static class Roles
{
    public const string Customer = "customer";
    public const string Admin = "admin";

    public static readonly IReadOnlyList<string> All = [Customer, Admin];
}
