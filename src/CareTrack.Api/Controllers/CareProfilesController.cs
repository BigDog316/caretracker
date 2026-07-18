using CareTrack.Api.Auth;
using CareTrack.Application;
using CareTrack.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareTrack.Api.Controllers;

/// <summary>
/// Demonstrates how feature endpoints attach access policies. Every route that
/// operates on a specific profile carries "{careProfileId:guid}" so the
/// authorization handler can resolve and check it before the action runs.
/// This is the pattern all future feature controllers (providers, appointments,
/// notes, documents, etc.) should follow.
/// </summary>
[ApiController]
[Authorize]
[Route("api/care-profiles")]
public sealed class CareProfilesController : ControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly CareProfileAccessService _access;

    public CareProfilesController(
        ICurrentUser currentUser, CareProfileAccessService access)
    {
        _currentUser = currentUser;
        _access = access;
    }

    /// <summary>Lists only the profiles the caller has access to.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<Guid>> ListMine(CancellationToken ct)
        => await _access.AccessibleProfileIdsAsync(_currentUser.RequireUserId(), ct);

    /// <summary>Viewer+ required. Handler enforces before this body runs.</summary>
    [HttpGet("{careProfileId:guid}")]
    [RequireCareProfile(AccessRole.Viewer)]
    public IActionResult Get(Guid careProfileId)
        => Ok(new { careProfileId, access = "viewer-ok" });

    /// <summary>Editor+ required for mutations.</summary>
    [HttpPut("{careProfileId:guid}")]
    [RequireCareProfile(AccessRole.Editor)]
    public IActionResult Update(Guid careProfileId)
        => Ok(new { careProfileId, access = "editor-ok" });

    /// <summary>Owner-only, e.g. managing sharing.</summary>
    [HttpPost("{careProfileId:guid}/grants")]
    [RequireCareProfile(AccessRole.Owner)]
    public IActionResult AddGrant(Guid careProfileId)
        => Ok(new { careProfileId, access = "owner-ok" });
}
