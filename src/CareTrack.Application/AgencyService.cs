using CareTrack.Domain;

namespace CareTrack.Application;

public sealed record CreateAgencyRequest(
    string Name, string Kind, string? ContactName,
    string? Phone, string? Email, string? Address, string? Notes);

/// <summary>
/// Agency operations (insurance, DD services, respite, ...). Mirrors the
/// provider slice: every method enforces access before touching data.
/// </summary>
public sealed class AgencyService
{
    private readonly ICareDataRepository _repo;
    private readonly CareProfileAccessService _access;

    public AgencyService(ICareDataRepository repo, CareProfileAccessService access)
    {
        _repo = repo;
        _access = access;
    }

    public async Task<Agency> AddAsync(
        Guid userId, Guid careProfileId, CreateAgencyRequest req,
        CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Editor, ct);

        var agency = new Agency
        {
            CareProfileId = careProfileId,
            Name = req.Name,
            Kind = string.IsNullOrWhiteSpace(req.Kind) ? AgencyKinds.Other : req.Kind,
            ContactName = req.ContactName,
            Phone = req.Phone,
            Email = req.Email,
            Address = req.Address,
            Notes = req.Notes
        };
        return await _repo.AddAgencyAsync(agency, ct);
    }

    public async Task<IReadOnlyList<Agency>> ListAsync(
        Guid userId, Guid careProfileId, string? kind = null,
        CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);
        return await _repo.ListAgenciesAsync(careProfileId, kind, ct);
    }

    public async Task<Agency?> GetAsync(
        Guid userId, Guid careProfileId, Guid agencyId, CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);
        var agency = await _repo.GetAgencyAsync(agencyId, ct);
        // Guard against an agency id belonging to a different profile.
        return agency?.CareProfileId == careProfileId ? agency : null;
    }
}
