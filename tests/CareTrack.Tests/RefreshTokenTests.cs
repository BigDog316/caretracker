using CareTrack.Infrastructure.Identity;
using Xunit;

namespace CareTrack.Tests;

/// <summary>
/// Unit tests for refresh-token validity. The full register/login/refresh flow
/// is covered by integration tests against a real database (Identity's
/// UserManager and password hashing need the EF stores); these pin the pure
/// validity logic that rotation depends on.
/// </summary>
public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-01-15T12:00:00Z");

    private static RefreshToken Make(
        DateTimeOffset expires, DateTimeOffset? revoked = null)
        => new()
        {
            UserId = Guid.NewGuid(),
            Token = "t",
            ExpiresAt = expires,
            RevokedAt = revoked
        };

    [Fact]
    public void Active_when_unexpired_and_not_revoked()
        => Assert.True(Make(Now.AddDays(1)).IsActive(Now));

    [Fact]
    public void Inactive_when_expired()
        => Assert.False(Make(Now.AddDays(-1)).IsActive(Now));

    [Fact]
    public void Inactive_when_revoked()
        => Assert.False(Make(Now.AddDays(1), revoked: Now.AddMinutes(-1)).IsActive(Now));

    [Fact]
    public void Inactive_exactly_at_expiry()
        => Assert.False(Make(Now).IsActive(Now));
}
