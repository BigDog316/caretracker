using CareTrack.Domain;

namespace CareTrack.Application;

/// <summary>
/// Abstraction over grant storage. The Infrastructure layer supplies an
/// EF Core implementation; tests supply an in-memory fake. Keeping this an
/// interface lets the access-scoping logic be unit-tested without a database.
/// </summary>
public interface IAccessGrantStore
{
    /// <summary>
    /// Returns the active grant linking <paramref name="userId"/> to
    /// <paramref name="careProfileId"/>, or null if none exists.
    /// Revoked grants must not be returned.
    /// </summary>
    Task<AccessGrant?> FindActiveGrantAsync(
        Guid userId, Guid careProfileId, CancellationToken ct = default);

    /// <summary>
    /// Returns the ids of every care profile the user currently has active
    /// access to. Used to scope list queries.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListAccessibleProfileIdsAsync(
        Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Returns every user holding an active (non-revoked) grant on the profile,
    /// with the contact fields delivery needs. Used by the reminder dispatcher
    /// to address prompts; revoked grants must not be returned.
    /// </summary>
    Task<IReadOnlyList<ActiveGrantee>> ListActiveGranteesAsync(
        Guid careProfileId, CancellationToken ct = default);
}

/// <summary>An active grant holder on a profile, flattened for delivery.</summary>
public sealed record ActiveGrantee(
    Guid UserId, string Email, string DisplayName, AccessRole Role);
