using CareTrack.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CareTrack.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/reminders")]
public sealed class RemindersController : ControllerBase
{
    private readonly FollowUpReminderService _service;
    private readonly ICurrentUser _user;

    public RemindersController(FollowUpReminderService service, ICurrentUser user)
    {
        _service = service;
        _user = user;
    }

    /// <summary>
    /// All pending "How did it go?" prompts across every profile the caller can
    /// access. No per-route profile id here: the service scopes internally to the
    /// user's accessible profiles.
    /// </summary>
    [HttpGet("follow-ups")]
    public async Task<IReadOnlyList<FollowUpReminder>> Pending(CancellationToken ct)
        => await _service.GetPendingAsync(_user.RequireUserId(), ct);
}
