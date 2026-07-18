using CareTrack.Application;
using CareTrack.Domain;
using Xunit;

namespace CareTrack.Tests;

/// <summary>
/// The reminder delivery sweep (milestone 3): due appointments produce
/// "How did it go?" prompts addressed only to active Editor+ grant holders,
/// throttled by the re-prompt interval, and stopping once the follow-up is
/// completed.
/// </summary>
public class FollowUpDeliveryTests
{
    private sealed class CapturingDelivery : IReminderDelivery
    {
        public List<FollowUpPrompt> Sent { get; } = new();

        public Task SendFollowUpPromptAsync(FollowUpPrompt prompt, CancellationToken ct = default)
        {
            Sent.Add(prompt);
            return Task.CompletedTask;
        }
    }

    private readonly Guid _editor = Guid.NewGuid();
    private readonly Guid _viewer = Guid.NewGuid();
    private readonly Guid _revoked = Guid.NewGuid();
    private readonly Guid _profile = Guid.NewGuid();

    private readonly InMemoryAccessGrantStore _grants = new();
    private readonly InMemoryCareDataRepository _repo = new();
    private readonly CapturingDelivery _delivery = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-01-15T12:00:00Z"));

    public FollowUpDeliveryTests()
    {
        _grants.Add(_editor, _profile, AccessRole.Editor, email: "editor@test.dev");
        _grants.Add(_viewer, _profile, AccessRole.Viewer, email: "viewer@test.dev");
        _grants.Add(_revoked, _profile, AccessRole.Owner, revoked: true,
            email: "revoked@test.dev");
    }

    private FollowUpReminderDispatcher Dispatcher(TimeSpan? reprompt = null)
        => new(_repo, _grants, _delivery, _clock,
            reprompt ?? TimeSpan.FromHours(24));

    private async Task<Appointment> AddEndedAppointmentAsync(
        string title = "Cardiology", double endedHoursAgo = 2)
    {
        var appt = new Appointment
        {
            CareProfileId = _profile,
            Title = title,
            StartsAt = _clock.UtcNow.AddHours(-endedHoursAgo - 1),
            EndsAt = _clock.UtcNow.AddHours(-endedHoursAgo)
        };
        await _repo.AddAppointmentAsync(appt);
        return appt;
    }

    [Fact]
    public async Task Prompts_go_to_editors_not_viewers_or_revoked()
    {
        var appt = await AddEndedAppointmentAsync();

        var sent = await Dispatcher().DispatchDueAsync();

        Assert.Equal(1, sent);
        var prompt = Assert.Single(_delivery.Sent);
        Assert.Equal(_editor, prompt.RecipientUserId);
        Assert.Equal("editor@test.dev", prompt.RecipientEmail);
        Assert.Equal(appt.Id, prompt.AppointmentId);
        Assert.Equal("Cardiology", prompt.AppointmentTitle);
        Assert.Equal(_clock.UtcNow, appt.FollowUpLastRemindedAt);
    }

    [Fact]
    public async Task Future_or_completed_appointments_produce_no_prompts()
    {
        await _repo.AddAppointmentAsync(new Appointment
        {
            CareProfileId = _profile,
            Title = "Upcoming",
            StartsAt = _clock.UtcNow.AddHours(1),
            EndsAt = _clock.UtcNow.AddHours(2)
        });
        var done = await AddEndedAppointmentAsync("Answered");
        done.FollowUpCompletedAt = _clock.UtcNow.AddHours(-1);

        var sent = await Dispatcher().DispatchDueAsync();

        Assert.Equal(0, sent);
        Assert.Empty(_delivery.Sent);
    }

    [Fact]
    public async Task Reprompt_waits_for_the_configured_interval()
    {
        await AddEndedAppointmentAsync();
        var dispatcher = Dispatcher(TimeSpan.FromHours(24));

        Assert.Equal(1, await dispatcher.DispatchDueAsync());
        Assert.Equal(0, await dispatcher.DispatchDueAsync()); // just prompted

        _clock.Advance(TimeSpan.FromHours(12));
        Assert.Equal(0, await dispatcher.DispatchDueAsync()); // still inside interval

        _clock.Advance(TimeSpan.FromHours(12));
        Assert.Equal(1, await dispatcher.DispatchDueAsync()); // interval elapsed
        Assert.Equal(2, _delivery.Sent.Count);
    }

    [Fact]
    public async Task Completing_followup_stops_reprompts()
    {
        var appt = await AddEndedAppointmentAsync();
        var dispatcher = Dispatcher();

        Assert.Equal(1, await dispatcher.DispatchDueAsync());

        appt.FollowUpCompletedAt = _clock.UtcNow;
        _clock.Advance(TimeSpan.FromDays(2));

        Assert.Equal(0, await dispatcher.DispatchDueAsync());
    }

    [Fact]
    public async Task Appointment_with_no_eligible_recipients_stays_unstamped()
    {
        var lonelyProfile = Guid.NewGuid(); // only a Viewer grant here
        _grants.Add(Guid.NewGuid(), lonelyProfile, AccessRole.Viewer);
        var appt = new Appointment
        {
            CareProfileId = lonelyProfile,
            Title = "Nobody to tell",
            StartsAt = _clock.UtcNow.AddHours(-3),
            EndsAt = _clock.UtcNow.AddHours(-2)
        };
        await _repo.AddAppointmentAsync(appt);

        var dispatcherSent = await Dispatcher().DispatchDueAsync();

        // The profile with an Editor still gets prompted normally elsewhere;
        // this appointment sends nothing and stays eligible for a future sweep.
        Assert.Equal(0, dispatcherSent);
        Assert.Null(appt.FollowUpLastRemindedAt);
    }

    [Fact]
    public async Task Multiple_editors_each_get_a_prompt()
    {
        var second = Guid.NewGuid();
        _grants.Add(second, _profile, AccessRole.Owner, email: "owner@test.dev");
        await AddEndedAppointmentAsync();

        var sent = await Dispatcher().DispatchDueAsync();

        Assert.Equal(2, sent);
        Assert.Equal(
            new[] { "editor@test.dev", "owner@test.dev" }.OrderBy(e => e),
            _delivery.Sent.Select(p => p.RecipientEmail).OrderBy(e => e));
    }
}
