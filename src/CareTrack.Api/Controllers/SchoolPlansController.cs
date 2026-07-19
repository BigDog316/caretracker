using CareTrack.Api.Auth;
using CareTrack.Application;
using CareTrack.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareTrack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/care-profiles/{careProfileId:guid}/school-plans")]
public sealed class SchoolPlansController : ControllerBase
{
    private readonly SchoolPlanService _service;
    private readonly ICurrentUser _user;

    public SchoolPlansController(SchoolPlanService service, ICurrentUser user)
    {
        _service = service;
        _user = user;
    }

    [HttpGet]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IReadOnlyList<Dtos.SchoolPlanDto>> List(
        Guid careProfileId, CancellationToken ct)
        => (await _service.ListAsync(_user.RequireUserId(), careProfileId, ct))
            .ToDtos(p => p.ToDto());

    /// <summary>Plans due for review within the window (default 60 days).</summary>
    [HttpGet("upcoming-reviews")]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IReadOnlyList<Dtos.SchoolPlanDto>> UpcomingReviews(
        Guid careProfileId, [FromQuery] int withinDays = 60,
        CancellationToken ct = default)
        => (await _service.ListUpcomingReviewsAsync(
            _user.RequireUserId(), careProfileId, withinDays, ct))
            .ToDtos(p => p.ToDto());

    [HttpGet("{planId:guid}")]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IActionResult> Get(
        Guid careProfileId, Guid planId, CancellationToken ct)
    {
        var plan = await _service.GetAsync(
            _user.RequireUserId(), careProfileId, planId, ct);
        return plan is null ? NotFound() : Ok(plan.ToDto());
    }

    [HttpPost]
    [RequireCareProfile(AccessRole.Editor)]
    public async Task<IActionResult> Create(
        Guid careProfileId, [FromBody] CreateSchoolPlanRequest req, CancellationToken ct)
    {
        var plan = await _service.AddAsync(
            _user.RequireUserId(), careProfileId, req, ct);
        return Created(
            $"api/care-profiles/{careProfileId}/school-plans/{plan.Id}", plan.ToDto());
    }

    public sealed record LinkDocumentBody(Guid DocumentId);

    /// <summary>Attaches an uploaded document (the signed plan) to the plan.</summary>
    [HttpPut("{planId:guid}/document")]
    [RequireCareProfile(AccessRole.Editor)]
    public async Task<IActionResult> LinkDocument(
        Guid careProfileId, Guid planId,
        [FromBody] LinkDocumentBody body, CancellationToken ct)
    {
        await _service.LinkDocumentAsync(
            _user.RequireUserId(), careProfileId, planId, body.DocumentId, ct);
        return NoContent();
    }
}
