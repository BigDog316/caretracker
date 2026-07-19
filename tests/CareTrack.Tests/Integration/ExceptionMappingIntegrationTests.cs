using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CareTrack.Domain;
using CareTrack.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CareTrack.Tests.Integration;

/// <summary>
/// Service-layer exceptions must map to proper HTTP statuses, never 500:
/// cross-profile entity-id mismatches → 404 (no existence leak), request
/// validation failures → 400.
/// </summary>
public sealed class ExceptionMappingIntegrationTests : IClassFixture<PostgresApiFactory>
{
    private const string SkipReason =
        "No reachable Postgres (set CARETRACK_TEST_DB or run one on localhost).";

    private readonly PostgresApiFactory _factory;

    public ExceptionMappingIntegrationTests(PostgresApiFactory factory)
        => _factory = factory;

    private sealed record AuthResultDto(
        Guid UserId, string AccessToken, string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt);

    private async Task<AuthResultDto> RegisterAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"exmap-{Guid.NewGuid():N}@test.dev",
            password = "ExMap!Passw0rd1",
            displayName = "Mapping Tester"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AuthResultDto>())!;
    }

    private async Task<Guid> SeedOwnedProfileAsync(Guid userId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CareTrackDbContext>();
        var profile = new CareProfile { DisplayName = name };
        db.CareProfiles.Add(profile);
        db.AccessGrants.Add(new AccessGrant
        {
            UserId = userId,
            CareProfileId = profile.Id,
            Role = AccessRole.Owner
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
    public async Task Cross_profile_appointment_id_is_404_not_500()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();
        var user = await RegisterAsync(client);

        // The caller legitimately owns both profiles, so the route-level
        // authorization passes; the appointment belongs to profile B but is
        // addressed under profile A.
        var profileA = await SeedOwnedProfileAsync(user.UserId, "Profile A");
        var profileB = await SeedOwnedProfileAsync(user.UserId, "Profile B");

        var create = await client.SendAsync(Authed(
            HttpMethod.Post, $"/api/care-profiles/{profileB}/appointments", user,
            new
            {
                title = "In profile B",
                startsAt = DateTimeOffset.UtcNow.AddDays(-1),
                endsAt = DateTimeOffset.UtcNow.AddHours(-23)
            }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
        var apptId = Guid.Parse(created!["id"]!.ToString()!);

        var followUp = await client.SendAsync(Authed(
            HttpMethod.Post,
            $"/api/care-profiles/{profileA}/appointments/{apptId}/follow-up",
            user, new { note = "wrong profile" }));
        Assert.Equal(HttpStatusCode.NotFound, followUp.StatusCode);

        var ics = await client.SendAsync(Authed(
            HttpMethod.Get,
            $"/api/care-profiles/{profileA}/appointments/{apptId}/calendar.ics",
            user));
        Assert.Equal(HttpStatusCode.NotFound, ics.StatusCode);
    }

    [SkippableFact]
    public async Task Appointment_ending_before_start_is_400()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();
        var user = await RegisterAsync(client);
        var profileId = await SeedOwnedProfileAsync(user.UserId, "Validation");

        var resp = await client.SendAsync(Authed(
            HttpMethod.Post, $"/api/care-profiles/{profileId}/appointments", user,
            new
            {
                title = "Backwards",
                startsAt = "2026-09-01T15:00:00Z",
                endsAt = "2026-09-01T14:00:00Z"
            }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Contains("end", body!["error"], StringComparison.OrdinalIgnoreCase);
    }
}
