using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CareTrack.Infrastructure.Push;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CareTrack.Tests.Integration;

/// <summary>Push subscription registration endpoints over the HTTP layer.</summary>
public sealed class PushSubscriptionIntegrationTests : IClassFixture<PostgresApiFactory>
{
    private const string SkipReason =
        "No reachable Postgres (set CARETRACK_TEST_DB or run one on localhost).";

    private readonly PostgresApiFactory _factory;

    public PushSubscriptionIntegrationTests(PostgresApiFactory factory)
        => _factory = factory;

    private sealed record AuthResultDto(
        Guid UserId, string AccessToken, string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt);

    private async Task<AuthResultDto> RegisterAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"push-{Guid.NewGuid():N}@test.dev",
            password = "Pu5h!Passw0rd",
            displayName = "Push Tester"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AuthResultDto>())!;
    }

    [SkippableFact]
    public async Task Subscribe_stores_and_unsubscribe_removes()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();
        var user = await RegisterAsync(client);
        var endpoint = $"https://push.example/{Guid.NewGuid():N}";

        var subscribe = new HttpRequestMessage(HttpMethod.Post, "/api/push-subscriptions")
        {
            Content = JsonContent.Create(new { endpoint, p256dh = "key", auth = "secret" })
        };
        subscribe.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", user.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.SendAsync(subscribe)).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPushSubscriptionStore>();
            var subs = await store.ListForUserAsync(user.UserId);
            Assert.Single(subs);
            Assert.Equal(endpoint, subs[0].Endpoint);
        }

        var unsubscribe = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/push-subscriptions?endpoint={Uri.EscapeDataString(endpoint)}");
        unsubscribe.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", user.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.SendAsync(unsubscribe)).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPushSubscriptionStore>();
            Assert.Empty(await store.ListForUserAsync(user.UserId));
        }
    }

    [SkippableFact]
    public async Task Public_key_requires_auth_and_returns_configured_key()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();

        var anon = await client.GetAsync("/api/push-subscriptions/public-key");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

        var user = await RegisterAsync(client);
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/push-subscriptions/public-key");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.False(string.IsNullOrWhiteSpace(body!["publicKey"]));
    }

    [SkippableFact]
    public async Task Anonymous_subscribe_is_unauthorized()
    {
        Skip.IfNot(PostgresApiFactory.IsPostgresAvailable, SkipReason);
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/push-subscriptions",
            new { endpoint = "https://push.example/x", p256dh = "k", auth = "a" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
