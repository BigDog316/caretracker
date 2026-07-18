namespace CareTrack.Infrastructure.Push;

/// <summary>
/// A Web Push subscription registered by one of a user's browsers/devices.
/// A user may hold several (laptop, phone, ...). Endpoint URLs are unique
/// across the system; subscriptions the push service reports as gone
/// (404/410) are pruned automatically during delivery.
/// </summary>
public class UserPushSubscription
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid UserId { get; set; }

    /// <summary>Push-service URL the browser handed out for this device.</summary>
    public required string Endpoint { get; set; }

    /// <summary>Client public key (P-256, base64url) for payload encryption.</summary>
    public required string P256dh { get; set; }

    /// <summary>Client auth secret (base64url) for payload encryption.</summary>
    public required string Auth { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
