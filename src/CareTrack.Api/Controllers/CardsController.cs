using CareTrack.Api.Auth;
using CareTrack.Application;
using CareTrack.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareTrack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/care-profiles/{careProfileId:guid}/cards")]
public sealed class CardsController : ControllerBase
{
    private readonly CardService _service;
    private readonly ICurrentUser _user;

    public CardsController(CardService service, ICurrentUser user)
    {
        _service = service;
        _user = user;
    }

    /// <summary>Suggested default sections for the "add card" UI.</summary>
    [HttpGet("sections")]
    [RequireCareProfile(AccessRole.Viewer)]
    public IReadOnlyList<string> Sections(Guid careProfileId) => _service.DefaultSections;

    [HttpGet]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IReadOnlyList<Card>> List(
        Guid careProfileId, [FromQuery] string? section, CancellationToken ct)
        => await _service.ListAsync(_user.RequireUserId(), careProfileId, section, ct);

    [HttpPost]
    [RequireCareProfile(AccessRole.Editor)]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Add(
        Guid careProfileId,
        IFormFile image,
        [FromForm] string section,
        [FromForm] string? label,
        [FromForm] string? description,
        CancellationToken ct)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { error = "A card image is required." });

        var req = new AddCardRequest(section, label, image.ContentType, description);
        await using var stream = image.OpenReadStream();
        var card = await _service.AddAsync(
            _user.RequireUserId(), careProfileId, req, stream, ct);

        return Created(
            $"api/care-profiles/{careProfileId}/cards/{card.Id}",
            new { card.Id, card.Section, card.Label });
    }

    [HttpGet("{cardId:guid}/image")]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IActionResult> Image(
        Guid careProfileId, Guid cardId, CancellationToken ct)
    {
        var img = await _service.ImageAsync(
            _user.RequireUserId(), careProfileId, cardId, ct);
        return img is null ? NotFound() : File(img.Content, img.ContentType);
    }

    [HttpDelete("{cardId:guid}")]
    [RequireCareProfile(AccessRole.Editor)]
    public async Task<IActionResult> Delete(
        Guid careProfileId, Guid cardId, CancellationToken ct)
    {
        await _service.DeleteAsync(_user.RequireUserId(), careProfileId, cardId, ct);
        return NoContent();
    }
}
