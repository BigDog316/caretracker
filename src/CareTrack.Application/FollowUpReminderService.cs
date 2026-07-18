using CareTrack.Domain;

namespace CareTrack.Application;

public sealed record FollowUpReminder(
    Guid AppointmentId, Guid CareProfileId, string Title,
    DateTimeOffset EndsAt, Guid? ProviderId);

/// <summary>
/// Produces the "How did it go?" reminders for a user: appointments that have
/// ended, across every profile they can access, that have no follow-up recorded
/// yet. The client turns each into a push/in-app prompt. Scoped to exactly the
/// user's accessible profiles — never a global scan.
/// </summary>
public sealed class FollowUpReminderService
{
    private readonly ICareDataRepository _repo;
    private readonly CareProfileAccessService _access;
    private readonly IClock _clock;

    public FollowUpReminderService(
        ICareDataRepository repo, CareProfileAccessService access, IClock clock)
    {
        _repo = repo;
        _access = access;
        _clock = clock;
    }

    public async Task<IReadOnlyList<FollowUpReminder>> GetPendingAsync(
        Guid userId, CancellationToken ct = default)
    {
        var profileIds = await _access.AccessibleProfileIdsAsync(userId, ct);
        if (profileIds.Count == 0)
            return Array.Empty<FollowUpReminder>();

        var due = await _repo.ListAwaitingFollowUpAsync(profileIds, _clock.UtcNow, ct);

        return due
            .OrderByDescending(a => a.EndsAt)
            .Select(a => new FollowUpReminder(
                a.Id, a.CareProfileId, a.Title, a.EndsAt, a.ProviderId))
            .ToList();
    }
}
