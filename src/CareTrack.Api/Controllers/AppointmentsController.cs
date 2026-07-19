using CareTrack.Api.Auth;
using CareTrack.Api;
using CareTrack.Application;
using CareTrack.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareTrack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/care-profiles/{careProfileId:guid}/appointments")]
public sealed class AppointmentsController : ControllerBase
{
    private readonly AppointmentService _service;
    private readonly ICurrentUser _user;

    public AppointmentsController(AppointmentService service, ICurrentUser user)
    {
        _service = service;
        _user = user;
    }

    [HttpGet]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IReadOnlyList<Dtos.AppointmentDto>> List(Guid careProfileId, CancellationToken ct)
        => (await _service.ListAsync(_user.RequireUserId(), careProfileId, ct))
            .ToDtos(a => a.ToDto());

    [HttpPost]
    [RequireCareProfile(AccessRole.Editor)]
    public async Task<IActionResult> Create(
        Guid careProfileId, [FromBody] CreateAppointmentRequest req, CancellationToken ct)
    {
        var appt = await _service.CreateAsync(_user.RequireUserId(), careProfileId, req, ct);
        return Created($"api/care-profiles/{careProfileId}/appointments/{appt.Id}", appt.ToDto());
    }

    /// <summary>Downloads the appointment as an .ics file (calendar fallback).</summary>
    [HttpGet("{appointmentId:guid}/calendar.ics")]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IActionResult> DownloadIcs(
        Guid careProfileId, Guid appointmentId, CancellationToken ct)
    {
        var ics = await _service.GetIcsAsync(
            _user.RequireUserId(), careProfileId, appointmentId, ct);
        return File(
            System.Text.Encoding.UTF8.GetBytes(ics),
            IcsCalendarWriter.ContentType,
            $"appointment-{appointmentId}.ics");
    }

    public sealed record FollowUpBody(string? Note);

    /// <summary>The "How did it go?" response: records a note and clears the prompt.</summary>
    [HttpPost("{appointmentId:guid}/follow-up")]
    [RequireCareProfile(AccessRole.Editor)]
    public async Task<IActionResult> CompleteFollowUp(
        Guid careProfileId, Guid appointmentId,
        [FromBody] FollowUpBody body, CancellationToken ct)
    {
        var note = await _service.CompleteFollowUpAsync(
            _user.RequireUserId(), careProfileId, appointmentId, body.Note, ct);
        return Ok(new { completed = true, noteId = note?.Id });
    }
}
