using System.Net;
using System.Text.Json;
using CareTrack.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPush;

namespace CareTrack.Infrastructure.Push;

public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";

    /// <summary>VAPID subject: a mailto: or https: URI identifying the sender.</summary>
    public string Subject { get; set; } = "";

    /// <summary>VAPID public key (base64url). Clients need this to subscribe.</summary>
    public string PublicKey { get; set; } = "";

    /// <summary>VAPID private key (base64url). Keep out of source control in prod.</summary>
    public string PrivateKey { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Subject)
        && !string.IsNullOrWhiteSpace(PublicKey)
        && !string.IsNullOrWhiteSpace(PrivateKey);
}

/// <summary>
/// Thin seam over the Web Push protocol client so delivery logic is testable.
/// Implementations throw <see cref="WebPushException"/> on push-service errors.
/// </summary>
public interface IWebPushSender
{
    Task SendAsync(
        string endpoint, string p256dh, string auth, string payload,
        CancellationToken ct = default);
}

public sealed class VapidWebPushSender : IWebPushSender
{
    private readonly WebPushClient _client = new();
    private readonly VapidDetails _vapid;

    public VapidWebPushSender(IOptions<WebPushOptions> options)
    {
        var o = options.Value;
        _vapid = new VapidDetails(o.Subject, o.PublicKey, o.PrivateKey);
    }

    public Task SendAsync(
        string endpoint, string p256dh, string auth, string payload,
        CancellationToken ct = default)
        => _client.SendNotificationAsync(
            new PushSubscription(endpoint, p256dh, auth), payload, _vapid, ct);
}

/// <summary>
/// Delivers "How did it go?" prompts as Web Push notifications to every
/// subscription the recipient has registered. Best-effort per subscription:
/// endpoints the push service reports gone (404/410) are pruned, other
/// failures are logged without failing the sweep.
/// </summary>
public sealed class WebPushReminderDelivery : IReminderDelivery
{
    private readonly IPushSubscriptionStore _subscriptions;
    private readonly IWebPushSender _sender;
    private readonly ILogger<WebPushReminderDelivery> _logger;

    public WebPushReminderDelivery(
        IPushSubscriptionStore subscriptions,
        IWebPushSender sender,
        ILogger<WebPushReminderDelivery> logger)
    {
        _subscriptions = subscriptions;
        _sender = sender;
        _logger = logger;
    }

    public async Task SendFollowUpPromptAsync(
        FollowUpPrompt prompt, CancellationToken ct = default)
    {
        var subs = await _subscriptions.ListForUserAsync(prompt.RecipientUserId, ct);
        if (subs.Count == 0)
        {
            _logger.LogDebug(
                "No push subscriptions for {UserId}; follow-up prompt for " +
                "appointment {AppointmentId} not delivered.",
                prompt.RecipientUserId, prompt.AppointmentId);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            title = "How did it go?",
            body = prompt.CareProfileDisplayName is null
                ? prompt.AppointmentTitle
                : $"{prompt.AppointmentTitle} — {prompt.CareProfileDisplayName}",
            data = new
            {
                appointmentId = prompt.AppointmentId,
                careProfileId = prompt.CareProfileId
            }
        });

        foreach (var sub in subs)
        {
            try
            {
                await _sender.SendAsync(sub.Endpoint, sub.P256dh, sub.Auth, payload, ct);
            }
            catch (WebPushException ex) when (
                ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                _logger.LogInformation(
                    "Pruning expired push subscription {SubscriptionId} for {UserId}.",
                    sub.Id, sub.UserId);
                await _subscriptions.RemoveAsync(sub, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Push delivery failed for subscription {SubscriptionId}; " +
                    "prompt will be re-sent on a later sweep.", sub.Id);
            }
        }
    }
}
