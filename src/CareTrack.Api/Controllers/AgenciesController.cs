using CareTrack.Api.Auth;
using CareTrack.Application;
using CareTrack.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareTrack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/care-profiles/{careProfileId:guid}/agencies")]
public sealed class AgenciesController : ControllerBase
{
    private readonly AgencyService _service;
    private readonly ICurrentUser _user;

    public AgenciesController(AgencyService service, ICurrentUser user)
    {
        _service = service;
        _user = user;
    }

    /// <summary>Lists agencies, optionally filtered by kind.</summary>
    [HttpGet]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IReadOnlyList<Agency>> List(
        Guid careProfileId, [FromQuery] string? kind, CancellationToken ct)
        => await _service.ListAsync(_user.RequireUserId(), careProfileId, kind, ct);

    [HttpGet("{agencyId:guid}")]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IActionResult> Get(
        Guid careProfileId, Guid agencyId, CancellationToken ct)
    {
        var agency = await _service.GetAsync(
            _user.RequireUserId(), careProfileId, agencyId, ct);
        return agency is null ? NotFound() : Ok(agency);
    }

    [HttpPost]
    [RequireCareProfile(AccessRole.Editor)]
    public async Task<IActionResult> Create(
        Guid careProfileId, [FromBody] CreateAgencyRequest req, CancellationToken ct)
    {
        var agency = await _service.AddAsync(
            _user.RequireUserId(), careProfileId, req, ct);
        return Created(
            $"api/care-profiles/{careProfileId}/agencies/{agency.Id}", agency);
    }

    /// <summary>Suggested kinds for the UI's picker.</summary>
    [HttpGet("kinds")]
    [RequireCareProfile(AccessRole.Viewer)]
    public IReadOnlyList<string> Kinds(Guid careProfileId) => AgencyKinds.Defaults;
}
