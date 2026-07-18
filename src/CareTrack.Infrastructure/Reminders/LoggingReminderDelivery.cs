using CareTrack.Application;
using Microsoft.Extensions.Logging;

namespace CareTrack.Infrastructure.Reminders;

/// <summary>
/// Dev-grade <see cref="IReminderDelivery"/> that writes prompts to the log.
/// Swap in a push/email implementation for production; the dispatcher and
/// hosted service don't change.
/// </summary>
public sealed class LoggingReminderDelivery : IReminderDelivery
{
    private readonly ILogger<LoggingReminderDelivery> _logger;

    public LoggingReminderDelivery(ILogger<LoggingReminderDelivery> logger)
        => _logger = logger;

    public Task SendFollowUpPromptAsync(FollowUpPrompt prompt, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Follow-up prompt: \"How did it go?\" for appointment {AppointmentTitle} " +
            "({AppointmentId}, ended {EndsAt:u}) on profile {ProfileName} -> {RecipientEmail}",
            prompt.AppointmentTitle, prompt.AppointmentId, prompt.EndsAt,
            prompt.CareProfileDisplayName ?? prompt.CareProfileId.ToString(),
            prompt.RecipientEmail);
        return Task.CompletedTask;
    }
}
