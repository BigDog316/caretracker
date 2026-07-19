using CareTrack.Api.Auth;
using CareTrack.Api;
using CareTrack.Application;
using CareTrack.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareTrack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/care-profiles/{careProfileId:guid}/providers")]
public sealed class ProvidersController : ControllerBase
{
    private readonly ProviderService _service;
    private readonly ICurrentUser _user;

    public ProvidersController(ProviderService service, ICurrentUser user)
    {
        _service = service;
        _user = user;
    }

    [HttpGet]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IReadOnlyList<Dtos.ProviderDto>> List(Guid careProfileId, CancellationToken ct)
        => (await _service.ListAsync(_user.RequireUserId(), careProfileId, ct))
            .ToDtos(p => p.ToDto());

    [HttpGet("{providerId:guid}")]
    [RequireCareProfile(AccessRole.Viewer)]
    public async Task<IActionResult> Get(Guid careProfileId, Guid providerId, CancellationToken ct)
    {
        var provider = await _service.GetAsync(_user.RequireUserId(), careProfileId, providerId, ct);
        return provider is null ? NotFound() : Ok(provider.ToDto());
    }

    [HttpPost]
    [RequireCareProfile(AccessRole.Editor)]
    public async Task<IActionResult> Create(
        Guid careProfileId, [FromBody] CreateProviderRequest req, CancellationToken ct)
    {
        var provider = await _service.AddAsync(_user.RequireUserId(), careProfileId, req, ct);
        return CreatedAtAction(nameof(Get),
            new { careProfileId, providerId = provider.Id }, provider.ToDto());
    }
}
