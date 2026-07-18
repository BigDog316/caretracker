namespace CareTrack.Domain;

/// <summary>
/// An individual being tracked (e.g. a child with special needs, an adult
/// with complex medical needs). This is the central entity of the system.
/// A profile is shared, not owned by a single user: any number of users may
/// hold an <see cref="AccessGrant"/> to it. All care data (providers,
/// appointments, notes, documents, etc.) hangs off a CareProfile and is
/// reachable only by users with an active grant.
/// </summary>
public class CareProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string DisplayName { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Grants that give users access to this profile.</summary>
    public ICollection<AccessGrant> AccessGrants { get; } = new List<AccessGrant>();
}
