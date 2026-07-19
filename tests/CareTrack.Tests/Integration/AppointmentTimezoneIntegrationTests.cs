using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CareTrack.Domain;
using CareTrack.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CareTrack.Tests.Integration;

/// <summary>
/// Regression test for a bug found in browser testing: clients send local
/// offsets (e.g. -04:00), and Npgsql rejects any non-UTC DateTimeOffset for
/// 'timestamp with time zone'. Creation must normalize to UTC.
/// </summary>
public sealed class AppointmentTimezoneIntegrationTests
    : IClassFixture<PostgresApiFactory>
{
    private const string SkipReason =
        "No reachable Postgres (set CARETRACK_TEST_DB or run one on localhost).";

    private readonly PostgresApiFactory _factory;

    public AppointmentTimezoneIntegrationTests(PostgresApiFactory factory)
        => _factory = factory;

    private sealed record AuthResultDto(
        Guid UserId, string AccessToken, string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt);

    [SkippableFact]
    public async Task Appointment_with_local_offset_is_stored_as_the_same_utc_instant()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();

        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"tz-{Guid.NewGuid():N}@test.dev",
            password = "Tz!Passw0rd123",
            displayName = "TZ Tester"
        });
        var user = (await register.Content.ReadFromJsonAsync<AuthResultDto>())!;

        Guid profileId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CareTrackDbContext>();
            var profile = new CareProfile { DisplayName = "TZ Kid" };
            db.CareProfiles.Add(profile);
            db.AccessGrants.Add(new AccessGrant
            {
                UserId = user.UserId,
                CareProfileId = profile.Id,
                Role = AccessRole.Owner
            });
            await db.SaveChangesAsync();
            profileId = profile.Id;
        }

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/care-profiles/{profileId}/appointments")
        {
            Content = JsonContent.Create(new
            {
                title = "Offset appointment",
                startsAt = "2026-08-01T10:30:00-04:00",
                endsAt = "2026-08-01T11:00:00-04:00"
            })
        };
        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", user.AccessToken);

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider
            .GetRequiredService<CareTrackDbContext>();
        var stored = verifyDb.Appointments.Single(
            a => a.CareProfileId == profileId);
        Assert.Equal(TimeSpan.Zero, stored.StartsAt.Offset);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-01T14:30:00Z"), stored.StartsAt);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-01T15:00:00Z"), stored.EndsAt);
    }
}
