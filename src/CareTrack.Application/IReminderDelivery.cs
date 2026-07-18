namespace CareTrack.Application;

/// <summary>
/// One "How did it go?" prompt addressed to one recipient. The dispatcher
/// produces these from the follow-up queue; a delivery implementation turns
/// them into push notifications, emails, etc.
/// </summary>
public sealed record FollowUpPrompt(
    Guid AppointmentId,
    Guid CareProfileId,
    string? CareProfileDisplayName,
    string AppointmentTitle,
    DateTimeOffset EndsAt,
    Guid RecipientUserId,
    string RecipientEmail,
    string RecipientDisplayName);

/// <summary>
/// Abstraction over prompt delivery channels (push, email). Implementations
/// live in Infrastructure; the dev default just logs. Delivery must be treated
/// as best-effort — a throw marks the sweep failed and the prompt is retried
/// on a later pass.
/// </summary>
public interface IReminderDelivery
{
    Task SendFollowUpPromptAsync(FollowUpPrompt prompt, CancellationToken ct = default);
}

/// <summary>Default that delivers nothing (no channel configured).</summary>
public sealed class NoOpReminderDelivery : IReminderDelivery
{
    public Task SendFollowUpPromptAsync(FollowUpPrompt prompt, CancellationToken ct = default)
        => Task.CompletedTask;
}
