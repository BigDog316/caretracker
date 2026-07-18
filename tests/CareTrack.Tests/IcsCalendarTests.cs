using CareTrack.Application;
using CareTrack.Domain;
using Xunit;

namespace CareTrack.Tests;

/// <summary>
/// The .ics download fallback: RFC 5545 output shape (UTC times, escaping,
/// line folding) and access scoping on <see cref="AppointmentService.GetIcsAsync"/>.
/// </summary>
public class IcsCalendarTests
{
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _outsider = Guid.NewGuid();
    private readonly Guid _profile = Guid.NewGuid();
    private readonly Guid _otherProfile = Guid.NewGuid();

    private readonly InMemoryAccessGrantStore _grants = new();
    private readonly InMemoryCareDataRepository _repo = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-01-15T12:00:00Z"));
    private readonly CareProfileAccessService _access;

    public IcsCalendarTests()
    {
        _access = new CareProfileAccessService(_grants);
        _grants.Add(_user, _profile, AccessRole.Viewer);
    }

    private AppointmentService Service()
        => new(_repo, _access, new NoOpCalendarSync(), _clock);

    private static Appointment MakeAppointment(Guid profileId, string title,
        string? location = null, Guid? providerId = null) => new()
    {
        CareProfileId = profileId,
        Title = title,
        Location = location,
        ProviderId = providerId,
        StartsAt = DateTimeOffset.Parse("2026-02-01T14:30:00Z"),
        EndsAt = DateTimeOffset.Parse("2026-02-01T15:00:00Z")
    };

    [Fact]
    public async Task Ics_contains_event_fields_in_utc()
    {
        var appt = MakeAppointment(_profile, "Cardiology check", "Suite 4");
        await _repo.AddAppointmentAsync(appt);

        var ics = await Service().GetIcsAsync(_user, _profile, appt.Id);

        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
        Assert.Contains($"UID:caretrack-{appt.Id}\r\n", ics);
        Assert.Contains("DTSTART:20260201T143000Z\r\n", ics);
        Assert.Contains("DTEND:20260201T150000Z\r\n", ics);
        Assert.Contains("DTSTAMP:20260115T120000Z\r\n", ics); // FixedClock
        Assert.Contains("SUMMARY:Cardiology check\r\n", ics);
        Assert.Contains("LOCATION:Suite 4\r\n", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
    }

    [Fact]
    public async Task Ics_converts_offset_times_to_utc()
    {
        var appt = new Appointment
        {
            CareProfileId = _profile,
            Title = "OT session",
            StartsAt = DateTimeOffset.Parse("2026-02-01T09:30:00-05:00"),
            EndsAt = DateTimeOffset.Parse("2026-02-01T10:00:00-05:00")
        };
        await _repo.AddAppointmentAsync(appt);

        var ics = await Service().GetIcsAsync(_user, _profile, appt.Id);

        Assert.Contains("DTSTART:20260201T143000Z", ics);
        Assert.Contains("DTEND:20260201T150000Z", ics);
    }

    [Fact]
    public async Task Ics_escapes_special_text_characters()
    {
        var appt = MakeAppointment(_profile, "Dentist; cleaning, x-ray\nbring forms");
        await _repo.AddAppointmentAsync(appt);

        var ics = await Service().GetIcsAsync(_user, _profile, appt.Id);

        Assert.Contains(@"SUMMARY:Dentist\; cleaning\, x-ray\nbring forms", ics);
    }

    [Fact]
    public async Task Ics_includes_provider_name_in_description()
    {
        var provider = new Provider { CareProfileId = _profile, Name = "Dr. Ada Lovelace" };
        await _repo.AddProviderAsync(provider);
        var appt = MakeAppointment(_profile, "Checkup", providerId: provider.Id);
        await _repo.AddAppointmentAsync(appt);

        var ics = await Service().GetIcsAsync(_user, _profile, appt.Id);

        Assert.Contains("DESCRIPTION:With Dr. Ada Lovelace", ics);
    }

    [Fact]
    public async Task Long_lines_fold_at_75_octets_with_continuation_space()
    {
        var appt = MakeAppointment(_profile, new string('a', 200));
        await _repo.AddAppointmentAsync(appt);

        var ics = await Service().GetIcsAsync(_user, _profile, appt.Id);

        foreach (var line in ics.Split("\r\n"))
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 75,
                $"Line exceeds 75 octets: {line}");

        // Unfolding (removing CRLF + space) restores the full summary.
        var unfolded = ics.Replace("\r\n ", "");
        Assert.Contains($"SUMMARY:{new string('a', 200)}", unfolded);
    }

    [Fact]
    public async Task Outsider_cannot_download_ics()
    {
        var appt = MakeAppointment(_profile, "Private");
        await _repo.AddAppointmentAsync(appt);

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Service().GetIcsAsync(_outsider, _profile, appt.Id));
    }

    [Fact]
    public async Task Appointment_from_another_profile_is_denied()
    {
        // Appointment lives in _otherProfile; caller has a grant on _profile
        // and passes _profile as the route scope — must not leak.
        var appt = MakeAppointment(_otherProfile, "Elsewhere");
        await _repo.AddAppointmentAsync(appt);

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Service().GetIcsAsync(_user, _profile, appt.Id));
    }
}
