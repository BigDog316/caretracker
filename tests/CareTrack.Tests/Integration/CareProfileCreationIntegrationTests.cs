using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace CareTrack.Tests.Integration;

/// <summary>
/// Profile creation over HTTP: the creator becomes Owner and can immediately
/// use profile-scoped routes; other users still get 403.
/// </summary>
public sealed class CareProfileCreationIntegrationTests
    : IClassFixture<PostgresApiFactory>
{
    private const string SkipReason =
        "No reachable Postgres (set CARETRACK_TEST_DB or run one on localhost).";

    private readonly PostgresApiFactory _factory;

    public CareProfileCreationIntegrationTests(PostgresApiFactory factory)
        => _factory = factory;

    private sealed record AuthResultDto(
        Guid UserId, string AccessToken, string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt);

    private async Task<AuthResultDto> RegisterAsync(HttpClient client, string label)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{label}-{Guid.NewGuid():N}@test.dev",
            password = "Pr0f!Passw0rd",
            displayName = label
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AuthResultDto>())!;
    }

    private static HttpRequestMessage Authed(
        HttpMethod method, string url, AuthResultDto who, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", who.AccessToken);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    [SkippableFact]
    public async Task Creator_owns_the_new_profile_and_others_are_denied()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();
        var creator = await RegisterAsync(client, "creator");
        var other = await RegisterAsync(client, "other");

        var create = await client.SendAsync(Authed(
            HttpMethod.Post, "/api/care-profiles", creator,
            new { displayName = "Integration Kiddo" }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var profileId = Guid.Parse(created!["id"].ToString()!);

        // Creator: listed with its display name, readable, and writable
        // (Owner ≥ Editor).
        var mine = await client.SendAsync(Authed(HttpMethod.Get, "/api/care-profiles", creator));
        var summaries = await mine.Content
            .ReadFromJsonAsync<List<Dictionary<string, object?>>>();
        Assert.Contains(summaries!, p =>
            Guid.Parse(p["id"]!.ToString()!) == profileId
            && p["displayName"]!.ToString() == "Integration Kiddo");

        var read = await client.SendAsync(Authed(
            HttpMethod.Get, $"/api/care-profiles/{profileId}", creator));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var addAgency = await client.SendAsync(Authed(
            HttpMethod.Post, $"/api/care-profiles/{profileId}/agencies", creator,
            new { name = "Acme Insurance", kind = "Insurance" }));
        Assert.Equal(HttpStatusCode.Created, addAgency.StatusCode);

        // Non-creator: 403 across the same routes.
        var denied = await client.SendAsync(Authed(
            HttpMethod.Get, $"/api/care-profiles/{profileId}", other));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var deniedList = await client.SendAsync(Authed(
            HttpMethod.Get, $"/api/care-profiles/{profileId}/school-plans", other));
        Assert.Equal(HttpStatusCode.Forbidden, deniedList.StatusCode);
    }

    [SkippableFact]
    public async Task Blank_display_name_is_a_400()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();
        var user = await RegisterAsync(client, "blank");

        var resp = await client.SendAsync(Authed(
            HttpMethod.Post, "/api/care-profiles", user, new { displayName = "  " }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
