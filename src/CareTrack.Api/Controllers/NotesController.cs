using CareTrack.Api.Auth;
using CareTrack.Api;
using CareTrack.Application;
using CareTrack.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareTrack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/care-profiles/{careProfileId:guid}/notes")]
public sealed class NotesController : ControllerBase
{
    private readonly NoteService _service;
    private readonly ICurrentUser _user;

    public NotesController(NoteService service, ICurrentUser user)
    {
        _service = service;
        _user = user;
    }

    /// <summary>List or keyword-search notes. Pass ?q=term to search.</summary>
    [HttpGet]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IReadOnlyList<Dtos.NoteDto>> List(
        Guid careProfileId, [FromQuery] string? q, CancellationToken ct)
        => (await _service.SearchAsync(_user.RequireUserId(), careProfileId, q ?? "", ct))
            .ToDtos(n => n.ToDto());

    /// <summary>Provider-filtered history. Repeat ?providerId= for several.</summary>
    [HttpGet("history")]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IReadOnlyList<Dtos.NoteDto>> History(
        Guid careProfileId, [FromQuery] Guid[] providerId, CancellationToken ct)
        => (await _service.ProviderHistoryAsync(
            _user.RequireUserId(), careProfileId, providerId, ct))
            .ToDtos(n => n.ToDto());

    [HttpPost]
    [RequireCareProfile(AccessRole.Editor)]
    public async Task<IActionResult> Create(
        Guid careProfileId, [FromBody] CreateNoteRequest req, CancellationToken ct)
    {
        var note = await _service.AddAsync(_user.RequireUserId(), careProfileId, req, ct);
        return Created($"api/care-profiles/{careProfileId}/notes/{note.Id}", note.ToDto());
    }
}
