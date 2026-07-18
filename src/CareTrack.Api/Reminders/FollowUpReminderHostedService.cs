using CareTrack.Application;
using Microsoft.Extensions.Options;

namespace CareTrack.Api.Reminders;

/// <summary>
/// Background job that periodically turns the follow-up queue into delivered
/// "How did it go?" prompts via <see cref="FollowUpReminderDispatcher"/>.
/// A failed sweep is logged and retried on the next tick — the loop must
/// never die.
/// </summary>
public sealed class FollowUpReminderHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<FollowUpReminderHostedService> _logger;
    private readonly FollowUpReminderOptions _options;

    public FollowUpReminderHostedService(
        IServiceScopeFactory scopes,
        IOptions<FollowUpReminderOptions> options,
        ILogger<FollowUpReminderHostedService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollInterval);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var dispatcher = scope.ServiceProvider
                    .GetRequiredService<FollowUpReminderDispatcher>();
                var sent = await dispatcher.DispatchDueAsync(stoppingToken);
                if (sent > 0)
                    _logger.LogInformation("Sent {Count} follow-up prompt(s).", sent);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Follow-up reminder sweep failed; will retry.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
