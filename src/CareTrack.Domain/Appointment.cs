namespace CareTrack.Domain;

/// <summary>
/// An appointment tied to a care profile and optionally a provider. Stored
/// indefinitely (never purged after it passes). Once the end time is reached the
/// system prompts the caregiver with "How did it go?" until they respond.
/// </summary>
public class Appointment
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid CareProfileId { get; init; }
    public CareProfile? CareProfile { get; init; }

    /// <summary>Optional link to the provider being seen.</summary>
    public Guid? ProviderId { get; set; }
    public Provider? Provider { get; init; }

    public required string Title { get; set; }
    public string? Location { get; set; }

    public required DateTimeOffset StartsAt { get; set; }
    public required DateTimeOffset EndsAt { get; set; }

    /// <summary>
    /// External calendar identifier once synced (e.g. Google/Apple event id),
    /// so edits and cancellations can be propagated. Null until synced.
    /// </summary>
    public string? ExternalCalendarEventId { get; set; }

    /// <summary>
    /// Set when the caregiver has answered the "How did it go?" prompt (whether
    /// by adding a note or explicitly dismissing). While null and the end time
    /// has passed, the appointment is "awaiting follow-up".
    /// </summary>
    public DateTimeOffset? FollowUpCompletedAt { get; set; }

    /// <summary>
    /// When the delivery job last pushed a "How did it go?" prompt for this
    /// appointment. Null until the first prompt goes out; used to throttle
    /// re-prompts to the configured interval.
    /// </summary>
    public DateTimeOffset? FollowUpLastRemindedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public ICollection<Note> Notes { get; } = new List<Note>();

    /// <summary>
    /// True when the appointment has ended but no follow-up has been recorded.
    /// Drives the "How did it go?" reminder queue.
    /// </summary>
    public bool IsAwaitingFollowUp(DateTimeOffset now)
        => FollowUpCompletedAt is null && EndsAt <= now;
}
