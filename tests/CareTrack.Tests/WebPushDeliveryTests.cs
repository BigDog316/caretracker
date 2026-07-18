using System.Net;
using CareTrack.Application;
using CareTrack.Infrastructure.Push;
using Microsoft.Extensions.Logging.Abstractions;
using WebPush;
using Xunit;

namespace CareTrack.Tests;

/// <summary>
/// Web Push delivery: prompts fan out to every subscription the recipient
/// registered, expired endpoints are pruned, and one bad endpoint never
/// fails the sweep.
/// </summary>
public class WebPushDeliveryTests
{
    private sealed class FakeSubscriptionStore : IPushSubscriptionStore
    {
        public List<UserPushSubscription> Subs { get; } = new();

        public Task<IReadOnlyList<UserPushSubscription>> ListForUserAsync(
            Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UserPushSubscription>>(
                Subs.Where(s => s.UserId == userId).ToList());

        public Task UpsertAsync(UserPushSubscription s, CancellationToken ct = default)
        { Subs.Add(s); return Task.CompletedTask; }

        public Task RemoveAsync(Guid userId, string endpoint, CancellationToken ct = default)
        { Subs.RemoveAll(s => s.UserId == userId && s.Endpoint == endpoint); return Task.CompletedTask; }

        public Task RemoveAsync(UserPushSubscription s, CancellationToken ct = default)
        { Subs.RemoveAll(x => x.Id == s.Id); return Task.CompletedTask; }
    }

    private sealed class FakeSender : IWebPushSender
    {
        public List<(string Endpoint, string Payload)> Sent { get; } = new();
        public Func<string, Exception?>? FailWith { get; set; }

        public Task SendAsync(string endpoint, string p256dh, string auth,
            string payload, CancellationToken ct = default)
        {
            if (FailWith?.Invoke(endpoint) is Exception ex) throw ex;
            Sent.Add((endpoint, payload));
            return Task.CompletedTask;
        }
    }

    private readonly Guid _user = Guid.NewGuid();
    private readonly FakeSubscriptionStore _store = new();
    private readonly FakeSender _sender = new();

    private WebPushReminderDelivery Delivery()
        => new(_store, _sender, NullLogger<WebPushReminderDelivery>.Instance);

    private FollowUpPrompt Prompt() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Kiddo", "Speech eval",
        DateTimeOffset.Parse("2026-01-15T10:00:00Z"),
        _user, "user@test.dev", "User");

    private void Subscribe(string endpoint, Guid? userId = null)
        => _store.Subs.Add(new UserPushSubscription
        {
            UserId = userId ?? _user,
            Endpoint = endpoint,
            P256dh = "p256dh-key",
            Auth = "auth-secret"
        });

    [Fact]
    public async Task Sends_payload_to_every_subscription_of_the_recipient()
    {
        Subscribe("https://push.example/laptop");
        Subscribe("https://push.example/phone");
        Subscribe("https://push.example/other-user", Guid.NewGuid());

        await Delivery().SendFollowUpPromptAsync(Prompt());

        Assert.Equal(2, _sender.Sent.Count);
        Assert.DoesNotContain(_sender.Sent, s => s.Endpoint.EndsWith("other-user"));
        Assert.All(_sender.Sent, s =>
        {
            using var doc = System.Text.Json.JsonDocument.Parse(s.Payload);
            Assert.Equal("How did it go?", doc.RootElement.GetProperty("title").GetString());
            Assert.Equal("Speech eval — Kiddo", doc.RootElement.GetProperty("body").GetString());
        });
    }

    [Fact]
    public async Task No_subscriptions_is_a_quiet_no_op()
    {
        await Delivery().SendFollowUpPromptAsync(Prompt());
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Gone_subscription_is_pruned_and_others_still_receive()
    {
        Subscribe("https://push.example/stale");
        Subscribe("https://push.example/alive");
        _sender.FailWith = ep => ep.EndsWith("stale")
            ? new WebPushException("Subscription gone",
                new PushSubscription("https://push.example/stale", "k", "a"),
                new HttpResponseMessage(HttpStatusCode.Gone))
            : null;

        await Delivery().SendFollowUpPromptAsync(Prompt());

        Assert.Single(_sender.Sent);
        Assert.Equal("https://push.example/alive", _sender.Sent[0].Endpoint);
        Assert.Single(_store.Subs.Where(s => s.UserId == _user));
        Assert.Equal("https://push.example/alive", _store.Subs.Single(s => s.UserId == _user).Endpoint);
    }

    [Fact]
    public async Task Transient_failure_keeps_subscription_and_does_not_throw()
    {
        Subscribe("https://push.example/flaky");
        Subscribe("https://push.example/ok");
        _sender.FailWith = ep => ep.EndsWith("flaky")
            ? new HttpRequestException("push service unavailable")
            : null;

        await Delivery().SendFollowUpPromptAsync(Prompt());

        Assert.Single(_sender.Sent);
        Assert.Equal(2, _store.Subs.Count); // nothing pruned
    }
}
