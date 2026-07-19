using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace CareTrack.Tests.Integration;

/// <summary>
/// Calendar endpoints when Google OAuth is NOT configured (the test host has
/// no credentials): status reports unavailable, connect refuses, and the
/// anonymous callback rejects unknown state.
/// </summary>
public sealed class CalendarEndpointIntegrationTests : IClassFixture<PostgresApiFactory>
{
    private const string SkipReason =
        "No reachable Postgres (set CARETRACK_TEST_DB or run one on localhost).";

    private readonly PostgresApiFactory _factory;

    public CalendarEndpointIntegrationTests(PostgresApiFactory factory)
        => _factory = factory;

    private sealed record AuthResultDto(
        Guid UserId, string AccessToken, string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt);

    [SkippableFact]
    public async Task Unconfigured_server_reports_unavailable_and_refuses_connect()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();

        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"cal-{Guid.NewGuid():N}@test.dev",
            password = "Cal!Passw0rd123",
            displayName = "Calendar Tester"
        });
        var user = (await register.Content.ReadFromJsonAsync<AuthResultDto>())!;

        var status = new HttpRequestMessage(HttpMethod.Get, "/api/calendar/google/status");
        status.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", user.AccessToken);
        var statusResp = await client.SendAsync(status);
        Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);
        var body = await statusResp.Content
            .ReadFromJsonAsync<Dictionary<string, bool>>();
        Assert.False(body!["available"]);
        Assert.False(body["connected"]);

        var connect = new HttpRequestMessage(HttpMethod.Get, "/api/calendar/google/connect");
        connect.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", user.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.SendAsync(connect)).StatusCode);
    }

    [SkippableFact]
    public async Task Anonymous_endpoints_behave()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();

        // Status requires auth.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/calendar/google/status")).StatusCode);

        // Callback is anonymous by design but rejects unknown state (404 here
        // because the test host has no Google credentials configured).
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/calendar/google/callback?state=bogus")).StatusCode);
    }
}
