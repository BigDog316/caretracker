namespace CareTrack.Domain;

/// <summary>
/// The many-to-many link that governs who can see and edit which care profile.
/// This is the security spine of the application: every read or write of care
/// data must be scoped through an active grant.
///
/// Example the model must support:
///   Caregiver A -> ChildA, ChildB, AdultC
///   Caregiver B -> ChildA, ChildD
///   Grandmother -> AdultC
/// </summary>
public class AccessGrant
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid UserId { get; init; }
    public User? User { get; init; }

    public required Guid CareProfileId { get; init; }
    public CareProfile? CareProfile { get; init; }

    public required AccessRole Role { get; set; }

    /// <summary>
    /// When set, the grant is inactive (revoked) and must be ignored by all
    /// access checks. Kept rather than hard-deleted for audit purposes.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset GrantedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>A grant is active when it has not been revoked.</summary>
    public bool IsActive => RevokedAt is null;

    /// <summary>True when this grant confers at least the requested role.</summary>
    public bool Allows(AccessRole required) => IsActive && Role >= required;
}
