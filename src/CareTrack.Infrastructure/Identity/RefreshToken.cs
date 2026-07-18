namespace CareTrack.Infrastructure.Identity;

/// <summary>
/// A persisted refresh token. Access tokens are short-lived JWTs; the client
/// exchanges a valid, unexpired, unrevoked refresh token for a new pair. Tokens
/// are stored hashed-at-rest in production; here the raw value is kept for
/// brevity — swap to a hash + lookup before shipping.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid UserId { get; init; }

    public required string Token { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>When this token was rotated, the id of its replacement.</summary>
    public Guid? ReplacedByTokenId { get; set; }

    public bool IsActive(DateTimeOffset now)
        => RevokedAt is null && ExpiresAt > now;
}
