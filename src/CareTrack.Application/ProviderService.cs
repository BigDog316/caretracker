using CareTrack.Domain;

namespace CareTrack.Application;

public sealed record CreateProviderRequest(
    string Name, string? Organization, string? Specialty,
    string? Address, string? Phone);

/// <summary>
/// Provider operations. Every method takes the acting user and enforces access
/// through <see cref="CareProfileAccessService"/> before touching data.
/// </summary>
public sealed class ProviderService
{
    private readonly ICareDataRepository _repo;
    private readonly CareProfileAccessService _access;

    public ProviderService(ICareDataRepository repo, CareProfileAccessService access)
    {
        _repo = repo;
        _access = access;
    }

    public async Task<Provider> AddAsync(
        Guid userId, Guid careProfileId, CreateProviderRequest req,
        CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Editor, ct);

        var provider = new Provider
        {
            CareProfileId = careProfileId,
            Name = req.Name,
            Organization = req.Organization,
            Specialty = req.Specialty,
            Address = req.Address,
            Phone = req.Phone
        };
        return await _repo.AddProviderAsync(provider, ct);
    }

    public async Task<IReadOnlyList<Provider>> ListAsync(
        Guid userId, Guid careProfileId, CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);
        return await _repo.ListProvidersAsync(careProfileId, ct);
    }

    public async Task<Provider?> GetAsync(
        Guid userId, Guid careProfileId, Guid providerId, CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);
        var provider = await _repo.GetProviderAsync(providerId, ct);
        // Guard against a provider id belonging to a different profile.
        return provider?.CareProfileId == careProfileId ? provider : null;
    }
}
