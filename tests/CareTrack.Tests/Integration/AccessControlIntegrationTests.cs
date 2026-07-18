using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CareTrack.Domain;
using CareTrack.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CareTrack.Tests.Integration;

/// <summary>
/// End-to-end access enforcement through the HTTP layer (handoff milestone 1):
/// a user without an active grant gets 403 on a profile-scoped route, and the
/// Owner ≥ Editor ≥ Viewer hierarchy holds over real requests.
/// </summary>
public sealed class AccessControlIntegrationTests : IClassFixture<PostgresApiFactory>
{
    private const string SkipReason =
        "No reachable Postgres (set CARETRACK_TEST_DB or run one on localhost).";

    private readonly PostgresApiFactory _factory;

    public AccessControlIntegrationTests(PostgresApiFactory factory) => _factory = factory;

    private sealed record AuthResultDto(
        Guid UserId, string AccessToken, string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt);

    private sealed record ProfileSummaryDto(Guid Id, string DisplayName);

    private async Task<AuthResultDto> RegisterAsync(HttpClient client, string label)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"{label}-{Guid.NewGuid():N}@test.dev",
            password = "Acc3ss!Passw0rd",
            displayName = label
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AuthResultDto>())!;
    }

    private async Task<Guid> SeedProfileWithGrantAsync(
        Guid userId, AccessRole role, DateTimeOffset? revokedAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CareTrackDbContext>();
        var profile = new CareProfile { DisplayName = "Integration Kid" };
        db.CareProfiles.Add(profile);
        db.AccessGrants.Add(new AccessGrant
        {
            UserId = userId,
            CareProfileId = profile.Id,
            Role = role,
            RevokedAt = revokedAt
        });
        await db.SaveChangesAsync();
        return profile.Id;
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
    public async Task User_without_grant_gets_403_on_profile_scoped_route()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();

        var owner = await RegisterAsync(client, "owner");
        var intruder = await RegisterAsync(client, "intruder");
        var profileId = await SeedProfileWithGrantAsync(owner.UserId, AccessRole.Owner);

        var ownerResp = await client.SendAsync(
            Authed(HttpMethod.Get, $"/api/care-profiles/{profileId}", owner));
        Assert.Equal(HttpStatusCode.OK, ownerResp.StatusCode);

        var intruderResp = await client.SendAsync(
            Authed(HttpMethod.Get, $"/api/care-profiles/{profileId}", intruder));
        Assert.Equal(HttpStatusCode.Forbidden, intruderResp.StatusCode);

        var intruderNotes = await client.SendAsync(
            Authed(HttpMethod.Get, $"/api/care-profiles/{profileId}/notes", intruder));
        Assert.Equal(HttpStatusCode.Forbidden, intruderNotes.StatusCode);
    }

    [SkippableFact]
    public async Task Anonymous_request_gets_401()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();

        var resp = await client.GetAsync($"/api/care-profiles/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [SkippableFact]
    public async Task Revoked_grant_is_not_honored()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();

        var user = await RegisterAsync(client, "revoked");
        var profileId = await SeedProfileWithGrantAsync(
            user.UserId, AccessRole.Owner, revokedAt: DateTimeOffset.UtcNow);

        var resp = await client.SendAsync(
            Authed(HttpMethod.Get, $"/api/care-profiles/{profileId}", user));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [SkippableFact]
    public async Task Viewer_cannot_use_editor_route_but_can_read()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();

        var viewer = await RegisterAsync(client, "viewer");
        var profileId = await SeedProfileWithGrantAsync(viewer.UserId, AccessRole.Viewer);

        var read = await client.SendAsync(
            Authed(HttpMethod.Get, $"/api/care-profiles/{profileId}", viewer));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var write = await client.SendAsync(
            Authed(HttpMethod.Put, $"/api/care-profiles/{profileId}", viewer));
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [SkippableFact]
    public async Task Profile_list_only_returns_granted_profiles()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();

        var granted = await RegisterAsync(client, "listed");
        var other = await RegisterAsync(client, "unlisted");
        var profileId = await SeedProfileWithGrantAsync(granted.UserId, AccessRole.Viewer);

        var grantedList = await client.SendAsync(
            Authed(HttpMethod.Get, "/api/care-profiles", granted));
        Assert.Equal(HttpStatusCode.OK, grantedList.StatusCode);
        var grantedProfiles = await grantedList.Content
            .ReadFromJsonAsync<List<ProfileSummaryDto>>();
        Assert.Contains(grantedProfiles!, p => p.Id == profileId);

        var otherList = await client.SendAsync(
            Authed(HttpMethod.Get, "/api/care-profiles", other));
        Assert.Equal(HttpStatusCode.OK, otherList.StatusCode);
        var otherProfiles = await otherList.Content
            .ReadFromJsonAsync<List<ProfileSummaryDto>>();
        Assert.DoesNotContain(otherProfiles!, p => p.Id == profileId);
    }
}
