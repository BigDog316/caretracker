using CareTrack.Domain;

namespace CareTrack.Application;

public sealed record CreateCareProfileRequest(
    string DisplayName, DateOnly? DateOfBirth);

public sealed record CareProfileSummary(
    Guid Id, string DisplayName, DateOnly? DateOfBirth);

/// <summary>
/// Care profile lifecycle. Creation is the one place a grant comes into being
/// without an existing Owner authorizing it: the creator becomes the profile's
/// first Owner, atomically with the profile itself, so no profile ever exists
/// without an owning grant.
/// </summary>
public sealed class CareProfileService
{
    private readonly ICareDataRepository _repo;
    private readonly CareProfileAccessService _access;

    public CareProfileService(ICareDataRepository repo, CareProfileAccessService access)
    {
        _repo = repo;
        _access = access;
    }

    /// <summary>Summaries of exactly the profiles the user can access.</summary>
    public async Task<IReadOnlyList<CareProfileSummary>> ListMineAsync(
        Guid userId, CancellationToken ct = default)
    {
        var ids = await _access.AccessibleProfileIdsAsync(userId, ct);
        if (ids.Count == 0) return Array.Empty<CareProfileSummary>();

        var profiles = await _repo.ListCareProfilesAsync(ids.ToList(), ct);
        return profiles
            .OrderBy(p => p.DisplayName)
            .Select(p => new CareProfileSummary(p.Id, p.DisplayName, p.DateOfBirth))
            .ToList();
    }

    public async Task<CareProfile> CreateAsync(
        Guid userId, CreateCareProfileRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            throw new ArgumentException("A care profile needs a display name.");

        var profile = new CareProfile
        {
            DisplayName = req.DisplayName.Trim(),
            DateOfBirth = req.DateOfBirth
        };
        var ownerGrant = new AccessGrant
        {
            UserId = userId,
            CareProfileId = profile.Id,
            Role = AccessRole.Owner
        };

        await _repo.CreateCareProfileAsync(profile, ownerGrant, ct);
        return profile;
    }
}
