using CareTrack.Domain;

namespace CareTrack.Application;

/// <summary>
/// Abstraction over external calendar providers (Google, Apple/EventKit) plus an
/// .ics fallback. Sync is per-user: the acting user's connected calendar (if
/// any) receives the event. Implementations return the external event id so
/// the appointment can be updated or cancelled later, or null when the user
/// has no calendar connected. Sync is best-effort — a failure must never block
/// the appointment itself.
/// </summary>
public interface ICalendarSync
{
    Task<string?> CreateEventAsync(
        Guid userId, Appointment appointment, CancellationToken ct = default);
    Task UpdateEventAsync(
        Guid userId, Appointment appointment, CancellationToken ct = default);
    Task CancelEventAsync(
        Guid userId, string externalEventId, CancellationToken ct = default);
}

/// <summary>Default that performs no external sync (calendar not connected).</summary>
public sealed class NoOpCalendarSync : ICalendarSync
{
    public Task<string?> CreateEventAsync(Guid userId, Appointment a, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
    public Task UpdateEventAsync(Guid userId, Appointment a, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task CancelEventAsync(Guid userId, string id, CancellationToken ct = default)
        => Task.CompletedTask;
}
