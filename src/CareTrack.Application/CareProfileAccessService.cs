using CareTrack.Domain;

namespace CareTrack.Application;

/// <summary>
/// The single chokepoint every read and write of care data must pass through.
/// Higher-level services call <see cref="RequireAccessAsync"/> before touching
/// any data belonging to a care profile. Centralizing the check here means the
/// authorization rule lives in exactly one place and can be exhaustively tested.
/// </summary>
public sealed class CareProfileAccessService
{
    private readonly IAccessGrantStore _grants;

    public CareProfileAccessService(IAccessGrantStore grants) => _grants = grants;

    /// <summary>
    /// Returns true if the user holds an active grant of at least the required
    /// role for the profile. Does not throw.
    /// </summary>
    public async Task<bool> HasAccessAsync(
        Guid userId,
        Guid careProfileId,
        AccessRole required = AccessRole.Viewer,
        CancellationToken ct = default)
    {
        var grant = await _grants.FindActiveGrantAsync(userId, careProfileId, ct);
        return grant is not null && grant.Allows(required);
    }

    /// <summary>
    /// Ensures the user may act on the profile at the required role, throwing
    /// <see cref="AccessDeniedException"/> otherwise. Call this at the top of
    /// any operation that reads or mutates care data.
    /// </summary>
    public async Task RequireAccessAsync(
        Guid userId,
        Guid careProfileId,
        AccessRole required = AccessRole.Viewer,
        CancellationToken ct = default)
    {
        if (!await HasAccessAsync(userId, careProfileId, required, ct))
            throw new AccessDeniedException();
    }

    /// <summary>
    /// Returns the set of profile ids the user may view. List/search queries
    /// must intersect their results with this set rather than returning all rows.
    /// </summary>
    public Task<IReadOnlyList<Guid>> AccessibleProfileIdsAsync(
        Guid userId, CancellationToken ct = default)
        => _grants.ListAccessibleProfileIdsAsync(userId, ct);
}
