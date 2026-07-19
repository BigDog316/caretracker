using CareTrack.Api.Auth;
using CareTrack.Api;
using CareTrack.Application;
using CareTrack.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareTrack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/care-profiles/{careProfileId:guid}/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly DocumentService _service;
    private readonly ICurrentUser _user;

    public DocumentsController(DocumentService service, ICurrentUser user)
    {
        _service = service;
        _user = user;
    }

    [HttpGet]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IReadOnlyList<Dtos.DocumentDto>> List(
        Guid careProfileId, [FromQuery] string? tag, CancellationToken ct)
        => (await _service.ListAsync(_user.RequireUserId(), careProfileId, tag, ct))
            .ToDtos(d => d.ToDto());

    /// <summary>Multipart upload. Field "file" plus optional metadata fields.</summary>
    [HttpPost]
    [RequireCareProfile(AccessRole.Editor)]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        Guid careProfileId,
        IFormFile file,
        [FromForm] string? description,
        [FromForm] string[]? tags,
        [FromForm] Guid? providerId,
        [FromForm] Guid? appointmentId,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "A file is required." });

        var req = new UploadDocumentRequest(
            file.FileName, file.ContentType, description,
            tags ?? Array.Empty<string>(), providerId, appointmentId);

        await using var stream = file.OpenReadStream();
        var doc = await _service.UploadAsync(
            _user.RequireUserId(), careProfileId, req, stream, ct);

        return Created(
            $"api/care-profiles/{careProfileId}/documents/{doc.Id}",
            new { doc.Id, doc.FileName, doc.SizeBytes });
    }

    [HttpGet("{documentId:guid}/content")]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IActionResult> Download(
        Guid careProfileId, Guid documentId, CancellationToken ct)
    {
        var dl = await _service.DownloadAsync(
            _user.RequireUserId(), careProfileId, documentId, ct);
        return dl is null
            ? NotFound()
            : File(dl.Content, dl.ContentType, dl.FileName);
    }

    [HttpDelete("{documentId:guid}")]
    [RequireCareProfile(AccessRole.Editor)]
    public async Task<IActionResult> Delete(
        Guid careProfileId, Guid documentId, CancellationToken ct)
    {
        await _service.DeleteAsync(_user.RequireUserId(), careProfileId, documentId, ct);
        return NoContent();
    }
}
