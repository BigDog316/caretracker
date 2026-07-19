using CareTrack.Domain;

namespace CareTrack.Application;

public sealed record CreateAppointmentRequest(
    string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    Guid? ProviderId, string? Location);

/// <summary>
/// Appointment operations, including external calendar sync on create and the
/// "How did it go?" follow-up completion. Access-scoped throughout.
/// </summary>
public sealed class AppointmentService
{
    private readonly ICareDataRepository _repo;
    private readonly CareProfileAccessService _access;
    private readonly ICalendarSync _calendar;
    private readonly IClock _clock;

    public AppointmentService(
        ICareDataRepository repo,
        CareProfileAccessService access,
        ICalendarSync calendar,
        IClock clock)
    {
        _repo = repo;
        _access = access;
        _calendar = calendar;
        _clock = clock;
    }

    public async Task<Appointment> CreateAsync(
        Guid userId, Guid careProfileId, CreateAppointmentRequest req,
        CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Editor, ct);

        if (req.EndsAt < req.StartsAt)
            throw new ArgumentException("Appointment end must not precede its start.");

        var appt = new Appointment
        {
            CareProfileId = careProfileId,
            Title = req.Title,
            // Normalize to UTC: clients send their local offset, and the
            // Postgres 'timestamp with time zone' writer only accepts UTC.
            // Same instant either way.
            StartsAt = req.StartsAt.ToUniversalTime(),
            EndsAt = req.EndsAt.ToUniversalTime(),
            ProviderId = req.ProviderId,
            Location = req.Location
        };

        appt.ExternalCalendarEventId = await _calendar.CreateEventAsync(userId, appt, ct);
        return await _repo.AddAppointmentAsync(appt, ct);
    }

    public async Task<IReadOnlyList<Appointment>> ListAsync(
        Guid userId, Guid careProfileId, CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);
        return await _repo.ListAppointmentsAsync(careProfileId, ct);
    }

    /// <summary>
    /// Renders an appointment as a downloadable .ics document — the calendar
    /// fallback for users without a connected Google/Apple calendar. Requires
    /// Viewer access.
    /// </summary>
    public async Task<string> GetIcsAsync(
        Guid userId, Guid careProfileId, Guid appointmentId,
        CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);

        var appt = await _repo.GetAppointmentAsync(appointmentId, ct);
        if (appt is null || appt.CareProfileId != careProfileId)
            throw new AccessDeniedException();

        string? providerName = null;
        if (appt.ProviderId is Guid providerId)
            providerName = (await _repo.GetProviderAsync(providerId, ct))?.Name;

        return IcsCalendarWriter.Write(appt, _clock.UtcNow, providerName);
    }

    /// <summary>
    /// Marks the "How did it go?" prompt as handled. Optionally records the
    /// caregiver's note in the same step. Requires Editor access.
    /// </summary>
    public async Task<Note?> CompleteFollowUpAsync(
        Guid userId, Guid careProfileId, Guid appointmentId, string? noteBody,
        CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Editor, ct);

        var appt = await _repo.GetAppointmentAsync(appointmentId, ct);
        if (appt is null || appt.CareProfileId != careProfileId)
            throw new AccessDeniedException();

        Note? note = null;
        if (!string.IsNullOrWhiteSpace(noteBody))
        {
            note = new Note
            {
                CareProfileId = careProfileId,
                AppointmentId = appointmentId,
                ProviderId = appt.ProviderId,
                Body = noteBody.Trim(),
                AuthorUserId = userId
            };
            await _repo.AddNoteAsync(note, ct);
        }

        appt.FollowUpCompletedAt = _clock.UtcNow;
        await _repo.UpdateAppointmentAsync(appt, ct);
        return note;
    }
}
