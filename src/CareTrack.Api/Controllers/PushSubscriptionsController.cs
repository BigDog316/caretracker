using CareTrack.Application;
using CareTrack.Infrastructure.Push;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CareTrack.Api.Controllers;

/// <summary>
/// Lets a signed-in user register their browser/device for "How did it go?"
/// push prompts. The client obtains the VAPID public key here, subscribes via
/// the Push API in the service worker, then posts the resulting subscription.
/// </summary>
[ApiController]
[Authorize]
[Route("api/push-subscriptions")]
public sealed class PushSubscriptionsController : ControllerBase
{
    private readonly IPushSubscriptionStore _store;
    private readonly ICurrentUser _user;
    private readonly WebPushOptions _options;

    public PushSubscriptionsController(
        IPushSubscriptionStore store,
        ICurrentUser user,
        IOptions<WebPushOptions> options)
    {
        _store = store;
        _user = user;
        _options = options.Value;
    }

    /// <summary>The VAPID public key clients need to subscribe.</summary>
    [HttpGet("public-key")]
    public IActionResult PublicKey()
        => _options.IsConfigured
            ? Ok(new { publicKey = _options.PublicKey })
            : NotFound(new { error = "Push is not configured on this server." });

    public sealed record SubscribeBody(string Endpoint, string P256dh, string Auth);

    [HttpPost]
    public async Task<IActionResult> Subscribe(
        [FromBody] SubscribeBody body, CancellationToken ct)
    {
        await _store.UpsertAsync(new UserPushSubscription
        {
            UserId = _user.RequireUserId(),
            Endpoint = body.Endpoint,
            P256dh = body.P256dh,
            Auth = body.Auth
        }, ct);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Unsubscribe(
        [FromQuery] string endpoint, CancellationToken ct)
    {
        await _store.RemoveAsync(_user.RequireUserId(), endpoint, ct);
        return NoContent();
    }
}
