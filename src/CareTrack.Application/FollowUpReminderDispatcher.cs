using CareTrack.Domain;

namespace CareTrack.Application;

public sealed class FollowUpReminderOptions
{
    public const string SectionName = "FollowUpReminders";

    /// <summary>How often the hosted service sweeps for due prompts.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Minimum gap between prompts for the same appointment while it remains
    /// unanswered.
    /// </summary>
    public TimeSpan RepromptInterval { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Turns the "How did it go?" follow-up queue into delivered prompts. This is
/// the system-side counterpart of <see cref="FollowUpReminderService"/>: it
/// sweeps all due appointments, but delivery is still access-scoped — prompts
/// go only to users holding an active Editor-or-better grant on the profile
/// (Editor is what completing a follow-up requires), never to revoked or
/// Viewer grants.
/// </summary>
public sealed class FollowUpReminderDispatcher
{
    private readonly ICareDataRepository _repo;
    private readonly IAccessGrantStore _grants;
    private readonly IReminderDelivery _delivery;
    private readonly IClock _clock;
    private readonly TimeSpan _repromptInterval;

    public FollowUpReminderDispatcher(
        ICareDataRepository repo,
        IAccessGrantStore grants,
        IReminderDelivery delivery,
        IClock clock,
        TimeSpan? repromptInterval = null)
    {
        _repo = repo;
        _grants = grants;
        _delivery = delivery;
        _clock = clock;
        _repromptInterval = repromptInterval ?? new FollowUpReminderOptions().RepromptInterval;
    }

    /// <summary>
    /// Sends prompts for every due appointment and stamps the ones that were
    /// actually delivered to someone. Returns the number of prompts sent.
    /// </summary>
    public async Task<int> DispatchDueAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var due = await _repo.ListDueForFollowUpReminderAsync(now, _repromptInterval, ct);
        var sent = 0;

        foreach (var appt in due)
        {
            var grantees = await _grants.ListActiveGranteesAsync(appt.CareProfileId, ct);
            var recipients = grantees.Where(g => g.Role >= AccessRole.Editor).ToList();
            if (recipients.Count == 0)
                continue; // nobody to prompt; leave unstamped so a later grant still gets one

            foreach (var recipient in recipients)
            {
                await _delivery.SendFollowUpPromptAsync(new FollowUpPrompt(
                    appt.Id,
                    appt.CareProfileId,
                    appt.CareProfile?.DisplayName,
                    appt.Title,
                    appt.EndsAt,
                    recipient.UserId,
                    recipient.Email,
                    recipient.DisplayName), ct);
                sent++;
            }

            appt.FollowUpLastRemindedAt = now;
            await _repo.UpdateAppointmentAsync(appt, ct);
        }

        return sent;
    }
}
