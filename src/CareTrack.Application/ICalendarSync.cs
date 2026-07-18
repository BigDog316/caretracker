using CareTrack.Domain;

namespace CareTrack.Application;

/// <summary>
/// Abstraction over external calendar providers (Google, Apple/EventKit) plus an
/// .ics fallback. Implementations return the external event id so the appointment
/// can be updated or cancelled later. The no-op implementation is used until a
/// user connects a calendar.
/// </summary>
public interface ICalendarSync
{
    Task<string?> CreateEventAsync(Appointment appointment, CancellationToken ct = default);
    Task UpdateEventAsync(Appointment appointment, CancellationToken ct = default);
    Task CancelEventAsync(string externalEventId, CancellationToken ct = default);
}

/// <summary>Default that performs no external sync (calendar not connected).</summary>
public sealed class NoOpCalendarSync : ICalendarSync
{
    public Task<string?> CreateEventAsync(Appointment a, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
    public Task UpdateEventAsync(Appointment a, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task CancelEventAsync(string id, CancellationToken ct = default)
        => Task.CompletedTask;
}
