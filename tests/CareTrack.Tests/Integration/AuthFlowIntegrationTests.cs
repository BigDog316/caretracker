using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace CareTrack.Tests.Integration;

/// <summary>
/// Full register → login → refresh → logout flow through the HTTP layer
/// against a real Postgres (handoff milestone 1).
/// </summary>
public sealed class AuthFlowIntegrationTests : IClassFixture<PostgresApiFactory>
{
    private const string SkipReason =
        "No reachable Postgres (set CARETRACK_TEST_DB or run one on localhost).";

    private readonly PostgresApiFactory _factory;

    public AuthFlowIntegrationTests(PostgresApiFactory factory) => _factory = factory;

    private sealed record AuthResultDto(
        Guid UserId,
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt);

    [SkippableFact]
    public async Task Register_login_refresh_logout_full_flow()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();
        var email = $"flow-{Guid.NewGuid():N}@test.dev";

        // Register issues a usable token pair.
        var registerResp = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Fl0w!Passw0rd", displayName = "Flow Tester" });
        Assert.Equal(HttpStatusCode.OK, registerResp.StatusCode);
        var registered = await registerResp.Content.ReadFromJsonAsync<AuthResultDto>();
        Assert.NotNull(registered);
        Assert.NotEqual(Guid.Empty, registered!.UserId);
        Assert.False(string.IsNullOrWhiteSpace(registered.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(registered.RefreshToken));

        // Login issues a fresh pair for the same user.
        var loginResp = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "Fl0w!Passw0rd" });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var loggedIn = (await loginResp.Content.ReadFromJsonAsync<AuthResultDto>())!;
        Assert.Equal(registered.UserId, loggedIn.UserId);
        Assert.NotEqual(registered.RefreshToken, loggedIn.RefreshToken);

        // Refresh rotates: new pair comes back, old refresh token is dead.
        var refreshResp = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = loggedIn.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);
        var refreshed = (await refreshResp.Content.ReadFromJsonAsync<AuthResultDto>())!;
        Assert.Equal(registered.UserId, refreshed.UserId);
        Assert.NotEqual(loggedIn.RefreshToken, refreshed.RefreshToken);

        var replayResp = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = loggedIn.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replayResp.StatusCode);

        // Logout revokes the current refresh token.
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { refreshToken = refreshed.RefreshToken })
        };
        logoutReq.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        var logoutResp = await client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.NoContent, logoutResp.StatusCode);

        var afterLogout = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = refreshed.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [SkippableFact]
    public async Task Register_with_duplicate_email_fails()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();
        var email = $"dupe-{Guid.NewGuid():N}@test.dev";
        var body = new { email, password = "Dup3!Passw0rd", displayName = "First" };

        var first = await client.PostAsJsonAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [SkippableFact]
    public async Task Login_with_wrong_password_is_unauthorized()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();
        var email = $"wrongpw-{Guid.NewGuid():N}@test.dev";

        var register = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "R1ght!Passw0rd", displayName = "PW Tester" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "Wr0ng!Passw0rd" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}
