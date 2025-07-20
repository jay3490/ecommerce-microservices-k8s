using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Outbox;
using Identity.Api.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Tests;

public sealed class AccountApiTests : IDisposable
{
    private readonly IdentityFactory _factory = new();
    private readonly HttpClient _client;

    public AccountApiTests() => _client = _factory.CreateClient();

    [Fact]
    public async Task Registration_returns_a_customer_token_that_validates_against_the_published_keys()
    {
        var tokens = await _factory.RegisterAsync(_client, "alice@example.com");

        Assert.Equal(["customer"], tokens.Roles);
        Assert.Equal("Bearer", tokens.TokenType);
        Assert.False(string.IsNullOrEmpty(tokens.RefreshToken));

        var discovery = await _client.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");
        Assert.Equal("https://identity.test", discovery.GetProperty("issuer").GetString());
        var jwksUri = new Uri(discovery.GetProperty("jwks_uri").GetString()!);
        var jwks = new JsonWebKeySet(await _client.GetStringAsync(jwksUri.PathAndQuery));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(tokens.AccessToken, new TokenValidationParameters
        {
            ValidIssuer = "https://identity.test",
            ValidAudience = "ecommerce-api",
            IssuerSigningKeys = jwks.GetSigningKeys(),
        });

        Assert.True(result.IsValid, result.Exception?.Message);
        var token = (JsonWebToken)result.SecurityToken;
        Assert.Equal("RS256", token.Alg);
        Assert.Equal("customer", token.GetClaim("role").Value);
        Assert.Equal("alice@example.com", token.GetClaim("email").Value);
        Assert.False(string.IsNullOrEmpty(token.Subject));
    }

    [Fact]
    public async Task Registration_records_a_user_registered_message_in_the_outbox()
    {
        await _factory.RegisterAsync(_client, "bob@example.com");

        await using var db = _factory.CreateDbContext();
        var message = await db.Set<OutboxMessage>().SingleAsync(m => m.Type == "UserRegistered");
        Assert.Contains("bob@example.com", message.Payload);
    }

    [Fact]
    public async Task An_email_can_only_be_registered_once()
    {
        await _factory.RegisterAsync(_client, "carol@example.com");

        var again = await _client.PostAsJsonAsync("/api/identity/register",
            new { email = "carol@example.com", password = "Another-Passw0rd", displayName = "Carol" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Weak_passwords_and_missing_fields_are_rejected()
    {
        var weak = await _client.PostAsJsonAsync("/api/identity/register",
            new { email = "dave@example.com", password = "short", displayName = "Dave" });
        var missing = await _client.PostAsJsonAsync("/api/identity/register", new { email = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task Login_checks_the_password_and_locks_the_account_after_repeated_failures()
    {
        await _factory.RegisterAsync(_client, "erin@example.com");

        var ok = await _client.PostAsJsonAsync("/api/identity/token", new { email = "erin@example.com", password = "Correct-Horse-9" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await _client.PostAsJsonAsync("/api/identity/token", new { email = "erin@example.com", password = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        var locked = await _client.PostAsJsonAsync("/api/identity/token", new { email = "erin@example.com", password = "Correct-Horse-9" });
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
    }

    [Fact]
    public async Task Unknown_accounts_get_the_same_answer_as_wrong_passwords()
    {
        var response = await _client.PostAsJsonAsync("/api/identity/token", new { email = "nobody@example.com", password = "whatever-123" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refreshing_rotates_the_refresh_token()
    {
        var first = await _factory.RegisterAsync(_client, "frank@example.com");

        var refreshed = await RefreshAsync(first.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = (await refreshed.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(second.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Reusing_a_refresh_token_revokes_the_whole_session()
    {
        var first = await _factory.RegisterAsync(_client, "grace@example.com");
        var second = (await (await RefreshAsync(first.RefreshToken)).Content.ReadFromJsonAsync<TokenResponse>())!;

        var replay = await RefreshAsync(first.RefreshToken);
        var successorAfterReplay = await RefreshAsync(second.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, successorAfterReplay.StatusCode);
    }

    [Fact]
    public async Task Revoking_ends_the_session_but_not_other_logins()
    {
        var registered = await _factory.RegisterAsync(_client, "heidi@example.com");
        var login = await _client.PostAsJsonAsync("/api/identity/token", new { email = "heidi@example.com", password = "Correct-Horse-9" });
        var otherSession = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;

        var revoke = await _client.PostAsJsonAsync("/api/identity/token/revoke", new { refreshToken = registered.RefreshToken });

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(registered.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(otherSession.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Garbage_refresh_tokens_are_rejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync("not-a-token")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(new string('x', 1000))).StatusCode);
    }

    [Fact]
    public async Task The_seeded_administrator_gets_both_roles()
    {
        var login = await _client.PostAsJsonAsync("/api/identity/token",
            new { email = IdentityFactory.AdminEmail, password = IdentityFactory.AdminPassword });

        var tokens = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.Equal(["admin", "customer"], tokens.Roles);
    }

    [Fact]
    public async Task Changing_roles_takes_effect_at_the_next_login_and_ends_existing_sessions()
    {
        var registered = await _factory.RegisterAsync(_client, "ivan@example.com");
        var userId = new JsonWebToken(registered.AccessToken).Subject;

        var update = await _client.PutAsJsonAsync($"/api/identity/users/{userId}/roles", new { roles = new[] { "customer", "admin" } });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var user = (await update.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.Equal(["admin", "customer"], user.Roles);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(registered.RefreshToken)).StatusCode);

        var login = await _client.PostAsJsonAsync("/api/identity/token", new { email = "ivan@example.com", password = "Correct-Horse-9" });
        Assert.Equal(["admin", "customer"], (await login.Content.ReadFromJsonAsync<TokenResponse>())!.Roles);
    }

    [Fact]
    public async Task Unknown_roles_cannot_be_assigned()
    {
        var registered = await _factory.RegisterAsync(_client, "judy@example.com");
        var userId = new JsonWebToken(registered.AccessToken).Subject;

        var update = await _client.PutAsJsonAsync($"/api/identity/users/{userId}/roles", new { roles = new[] { "root" } });

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
    }

    [Fact]
    public async Task Me_returns_the_profile_of_the_customer_the_gateway_identified()
    {
        var registered = await _factory.RegisterAsync(_client, "ken@example.com");
        var userId = new JsonWebToken(registered.AccessToken).Subject;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/me");
        request.Headers.Add("X-Customer-Id", userId);
        var response = await _client.SendAsync(request);

        var user = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.Equal("ken@example.com", user.Email);
        Assert.Equal(["customer"], user.Roles);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/identity/me")).StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        _client.PostAsJsonAsync("/api/identity/token/refresh", new { refreshToken });
}
