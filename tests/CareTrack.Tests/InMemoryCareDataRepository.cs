using CareTrack.Application;
using CareTrack.Domain;

namespace CareTrack.Tests;

/// <summary>
/// In-memory <see cref="ICareDataRepository"/> for unit tests. Search is a simple
/// case-insensitive substring match (the EF impl uses Postgres full-text search).
/// </summary>
internal sealed class InMemoryCareDataRepository : ICareDataRepository
{
    private readonly List<Provider> _providers = new();
    private readonly List<Appointment> _appointments = new();
    private readonly List<Note> _notes = new();

    public Task<Provider> AddProviderAsync(Provider p, CancellationToken ct = default)
    { _providers.Add(p); return Task.FromResult(p); }

    public Task<Provider?> GetProviderAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_providers.SingleOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Provider>> ListProvidersAsync(Guid profileId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Provider>>(
            _providers.Where(p => p.CareProfileId == profileId).ToList());

    public Task<Appointment> AddAppointmentAsync(Appointment a, CancellationToken ct = default)
    { _appointments.Add(a); return Task.FromResult(a); }

    public Task<Appointment?> GetAppointmentAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_appointments.SingleOrDefault(a => a.Id == id));

    public Task<IReadOnlyList<Appointment>> ListAppointmentsAsync(Guid profileId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Appointment>>(
            _appointments.Where(a => a.CareProfileId == profileId).ToList());

    public Task<IReadOnlyList<Appointment>> ListAwaitingFollowUpAsync(
        IEnumerable<Guid> profileIds, DateTimeOffset now, CancellationToken ct = default)
    {
        var set = profileIds.ToHashSet();
        IReadOnlyList<Appointment> due = _appointments
            .Where(a => set.Contains(a.CareProfileId)
                        && a.FollowUpCompletedAt is null
                        && a.EndsAt <= now)
            .ToList();
        return Task.FromResult(due);
    }

    public Task<IReadOnlyList<Appointment>> ListDueForFollowUpReminderAsync(
        DateTimeOffset now, TimeSpan repromptAfter, CancellationToken ct = default)
    {
        var repromptBefore = now - repromptAfter;
        IReadOnlyList<Appointment> due = _appointments
            .Where(a => a.FollowUpCompletedAt is null
                        && a.EndsAt <= now
                        && (a.FollowUpLastRemindedAt is null
                            || a.FollowUpLastRemindedAt <= repromptBefore))
            .OrderBy(a => a.EndsAt)
            .ToList();
        return Task.FromResult(due);
    }

    public Task UpdateAppointmentAsync(Appointment a, CancellationToken ct = default)
        => Task.CompletedTask; // mutations happen on the tracked instance in tests

    public Task<Note> AddNoteAsync(Note n, CancellationToken ct = default)
    { _notes.Add(n); return Task.FromResult(n); }

    public Task<IReadOnlyList<Note>> ListNotesAsync(Guid profileId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Note>>(
            _notes.Where(n => n.CareProfileId == profileId).ToList());

    public Task<IReadOnlyList<Note>> SearchNotesAsync(Guid profileId, string keyword, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Note>>(
            _notes.Where(n => n.CareProfileId == profileId
                              && n.Body.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                  .ToList());

    public Task<IReadOnlyList<Note>> ListNotesForProvidersAsync(
        Guid profileId, IReadOnlyCollection<Guid> providerIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Note>>(
            _notes.Where(n => n.CareProfileId == profileId
                              && n.ProviderId is not null
                              && providerIds.Contains(n.ProviderId.Value))
                  .ToList());

    private readonly List<Document> _documents = new();
    private readonly List<Card> _cards = new();

    public Task<Document> AddDocumentAsync(Document d, CancellationToken ct = default)
    { _documents.Add(d); return Task.FromResult(d); }

    public Task<Document?> GetDocumentAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_documents.SingleOrDefault(d => d.Id == id));

    public Task<IReadOnlyList<Document>> ListDocumentsAsync(
        Guid profileId, string? tag, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Document>>(
            _documents.Where(d => d.CareProfileId == profileId
                                  && (string.IsNullOrEmpty(tag)
                                      || d.Tags.Any(t => t.Value == tag)))
                      .ToList());

    public Task RemoveDocumentAsync(Document d, CancellationToken ct = default)
    { _documents.Remove(d); return Task.CompletedTask; }

    public Task<Card> AddCardAsync(Card c, CancellationToken ct = default)
    { _cards.Add(c); return Task.FromResult(c); }

    public Task<Card?> GetCardAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_cards.SingleOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<Card>> ListCardsAsync(
        Guid profileId, string? section, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Card>>(
            _cards.Where(c => c.CareProfileId == profileId
                              && (string.IsNullOrEmpty(section) || c.Section == section))
                  .ToList());

    public Task RemoveCardAsync(Card c, CancellationToken ct = default)
    { _cards.Remove(c); return Task.CompletedTask; }
}

/// <summary>Fixed clock for deterministic follow-up tests.</summary>
internal sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset now) => UtcNow = now;
    public DateTimeOffset UtcNow { get; set; }
    public void Advance(TimeSpan by) => UtcNow += by;
}
