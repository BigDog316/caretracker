namespace CareTrack.Domain;

/// <summary>
/// An account holder: a caregiver, family member, or a self-advocate
/// (an individual with disabilities managing their own profile).
/// A user gains access to a <see cref="CareProfile"/> only through an
/// <see cref="AccessGrant"/>.
/// </summary>
public class User
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Grants that give this user access to care profiles.</summary>
    public ICollection<AccessGrant> AccessGrants { get; } = new List<AccessGrant>();
}
