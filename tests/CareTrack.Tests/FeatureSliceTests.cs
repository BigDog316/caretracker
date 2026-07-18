using CareTrack.Application;
using CareTrack.Domain;
using Xunit;

namespace CareTrack.Tests;

/// <summary>
/// Tests the Providers + Appointments + Notes slice: access is enforced on every
/// operation, the "How did it go?" queue surfaces only ended un-followed-up
/// appointments in the caller's profiles, and completing a follow-up records the
/// note and clears the prompt.
/// </summary>
public class FeatureSliceTests
{
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _outsider = Guid.NewGuid();
    private readonly Guid _profile = Guid.NewGuid();
    private readonly Guid _otherProfile = Guid.NewGuid();

    private readonly InMemoryAccessGrantStore _grants = new();
    private readonly InMemoryCareDataRepository _repo = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-01-15T12:00:00Z"));
    private readonly CareProfileAccessService _access;

    public FeatureSliceTests()
    {
        _access = new CareProfileAccessService(_grants);
        _grants.Add(_user, _profile, AccessRole.Editor);
        _grants.Add(_user, _otherProfile, AccessRole.Viewer); // read-only elsewhere
    }

    private AppointmentService Appointments()
        => new(_repo, _access, new NoOpCalendarSync(), _clock);

    [Fact]
    public async Task Outsider_cannot_add_provider()
    {
        var svc = new ProviderService(_repo, _access);
        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            svc.AddAsync(_outsider, _profile,
                new CreateProviderRequest("Dr. X", null, null, null, null)));
    }

    [Fact]
    public async Task Viewer_cannot_add_appointment()
    {
        // _user is only a Viewer on _otherProfile.
        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Appointments().CreateAsync(_user, _otherProfile,
                new CreateAppointmentRequest("Checkup",
                    _clock.UtcNow, _clock.UtcNow.AddHours(1), null, null)));
    }

    [Fact]
    public async Task Ended_appointment_appears_in_followup_queue()
    {
        var appts = Appointments();
        // Ended two hours ago.
        await appts.CreateAsync(_user, _profile, new CreateAppointmentRequest(
            "Cardiology", _clock.UtcNow.AddHours(-3), _clock.UtcNow.AddHours(-2), null, null));
        // Still in the future.
        await appts.CreateAsync(_user, _profile, new CreateAppointmentRequest(
            "Future PT", _clock.UtcNow.AddHours(1), _clock.UtcNow.AddHours(2), null, null));

        var reminders = new FollowUpReminderService(_repo, _access, _clock);
        var pending = await reminders.GetPendingAsync(_user);

        Assert.Single(pending);
        Assert.Equal("Cardiology", pending[0].Title);
    }

    [Fact]
    public async Task Completing_followup_records_note_and_clears_prompt()
    {
        var appts = Appointments();
        var appt = await appts.CreateAsync(_user, _profile, new CreateAppointmentRequest(
            "Neurology", _clock.UtcNow.AddHours(-3), _clock.UtcNow.AddHours(-2), null, null));

        var reminders = new FollowUpReminderService(_repo, _access, _clock);
        Assert.Single(await reminders.GetPendingAsync(_user));

        var note = await appts.CompleteFollowUpAsync(
            _user, _profile, appt.Id, "Walked several feet independently. A first!");

        Assert.NotNull(note);
        Assert.NotNull(appt.FollowUpCompletedAt);
        Assert.Empty(await reminders.GetPendingAsync(_user));
    }

    [Fact]
    public async Task Followup_queue_never_leaks_across_users()
    {
        var appts = Appointments();
        await appts.CreateAsync(_user, _profile, new CreateAppointmentRequest(
            "Private appt", _clock.UtcNow.AddHours(-3), _clock.UtcNow.AddHours(-2), null, null));

        // Outsider has no grants at all -> empty queue.
        var reminders = new FollowUpReminderService(_repo, _access, _clock);
        Assert.Empty(await reminders.GetPendingAsync(_outsider));
    }

    [Fact]
    public async Task Note_search_is_scoped_and_matches_keyword()
    {
        var notes = new NoteService(_repo, _access);
        await notes.AddAsync(_user, _profile,
            new CreateNoteRequest("Consider an evaluation for dyslexia", null, null));
        await notes.AddAsync(_user, _profile,
            new CreateNoteRequest("Prescription refill needed", null, null));

        var hits = await notes.SearchAsync(_user, _profile, "dyslexia");
        Assert.Single(hits);
        Assert.Contains("dyslexia", hits[0].Body);
    }

    [Fact]
    public async Task Provider_history_filters_by_provider()
    {
        var providerSvc = new ProviderService(_repo, _access);
        var drA = await providerSvc.AddAsync(_user, _profile,
            new CreateProviderRequest("Dr. A", null, null, null, null));
        var drB = await providerSvc.AddAsync(_user, _profile,
            new CreateProviderRequest("Dr. B", null, null, null, null));

        var notes = new NoteService(_repo, _access);
        await notes.AddAsync(_user, _profile, new CreateNoteRequest("A note", null, drA.Id));
        await notes.AddAsync(_user, _profile, new CreateNoteRequest("B note", null, drB.Id));

        var onlyA = await notes.ProviderHistoryAsync(_user, _profile, new[] { drA.Id });
        Assert.Single(onlyA);
        Assert.Equal("A note", onlyA[0].Body);
    }

    [Fact]
    public async Task Appointment_end_before_start_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Appointments().CreateAsync(_user, _profile, new CreateAppointmentRequest(
                "Bad times", _clock.UtcNow, _clock.UtcNow.AddHours(-1), null, null)));
    }
}
