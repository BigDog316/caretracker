using CareTrack.Application;
using CareTrack.Domain;

namespace CareTrack.Tests;

/// <summary>
/// Simple in-memory grant store used to unit-test access scoping without a
/// database. Mirrors the revoked-grant filtering the EF implementation applies.
/// </summary>
internal sealed class InMemoryAccessGrantStore : IAccessGrantStore
{
    private readonly List<AccessGrant> _grants = new();

    private readonly Dictionary<Guid, (string Email, string DisplayName)> _contacts = new();

    public AccessGrant Add(Guid userId, Guid careProfileId, AccessRole role,
        bool revoked = false, string? email = null, string? displayName = null)
    {
        var grant = new AccessGrant
        {
            UserId = userId,
            CareProfileId = careProfileId,
            Role = role,
            RevokedAt = revoked ? DateTimeOffset.UtcNow : null
        };
        _grants.Add(grant);
        _contacts[userId] = (
            email ?? $"{userId:N}@test.dev",
            displayName ?? $"User {userId:N}"[..12]);
        return grant;
    }

    /// <summary>
    /// Adds a grant created elsewhere (e.g. by profile creation through the
    /// in-memory repository).
    /// </summary>
    public void AddExisting(AccessGrant grant)
    {
        _grants.Add(grant);
        if (!_contacts.ContainsKey(grant.UserId))
            _contacts[grant.UserId] = (
                $"{grant.UserId:N}@test.dev", "Test User");
    }

    public Task<AccessGrant?> FindActiveGrantAsync(
        Guid userId, Guid careProfileId, CancellationToken ct = default)
        => Task.FromResult(_grants.SingleOrDefault(
            g => g.UserId == userId
                 && g.CareProfileId == careProfileId
                 && g.RevokedAt is null));

    public Task<IReadOnlyList<Guid>> ListAccessibleProfileIdsAsync(
        Guid userId, CancellationToken ct = default)
    {
        IReadOnlyList<Guid> ids = _grants
            .Where(g => g.UserId == userId && g.RevokedAt is null)
            .Select(g => g.CareProfileId)
            .Distinct()
            .ToList();
        return Task.FromResult(ids);
    }

    public Task<IReadOnlyList<ActiveGrantee>> ListActiveGranteesAsync(
        Guid careProfileId, CancellationToken ct = default)
    {
        IReadOnlyList<ActiveGrantee> grantees = _grants
            .Where(g => g.CareProfileId == careProfileId && g.RevokedAt is null)
            .Select(g =>
            {
                var contact = _contacts[g.UserId];
                return new ActiveGrantee(
                    g.UserId, contact.Email, contact.DisplayName, g.Role);
            })
            .ToList();
        return Task.FromResult(grantees);
    }
}
