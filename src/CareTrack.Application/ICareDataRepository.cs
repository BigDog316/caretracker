using CareTrack.Domain;

namespace CareTrack.Application;

/// <summary>
/// Persistence abstraction for the feature slice. Infrastructure supplies the
/// EF implementation; tests can fake it. Access checks are NOT done here — they
/// are the caller's responsibility via <see cref="CareProfileAccessService"/>,
/// so this interface stays a thin data gateway.
/// </summary>
public interface ICareDataRepository
{
    // Providers
    Task<Provider> AddProviderAsync(Provider provider, CancellationToken ct = default);
    Task<Provider?> GetProviderAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Provider>> ListProvidersAsync(Guid careProfileId, CancellationToken ct = default);

    // Appointments
    Task<Appointment> AddAppointmentAsync(Appointment appt, CancellationToken ct = default);
    Task<Appointment?> GetAppointmentAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Appointment>> ListAppointmentsAsync(Guid careProfileId, CancellationToken ct = default);
    Task<IReadOnlyList<Appointment>> ListAwaitingFollowUpAsync(
        IEnumerable<Guid> careProfileIds, DateTimeOffset now, CancellationToken ct = default);
    Task UpdateAppointmentAsync(Appointment appt, CancellationToken ct = default);

    // Notes
    Task<Note> AddNoteAsync(Note note, CancellationToken ct = default);
    Task<IReadOnlyList<Note>> ListNotesAsync(Guid careProfileId, CancellationToken ct = default);
    Task<IReadOnlyList<Note>> SearchNotesAsync(
        Guid careProfileId, string keyword, CancellationToken ct = default);
    Task<IReadOnlyList<Note>> ListNotesForProvidersAsync(
        Guid careProfileId, IReadOnlyCollection<Guid> providerIds, CancellationToken ct = default);

    // Documents
    Task<Document> AddDocumentAsync(Document document, CancellationToken ct = default);
    Task<Document?> GetDocumentAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> ListDocumentsAsync(
        Guid careProfileId, string? tag, CancellationToken ct = default);
    Task RemoveDocumentAsync(Document document, CancellationToken ct = default);

    // Cards
    Task<Card> AddCardAsync(Card card, CancellationToken ct = default);
    Task<Card?> GetCardAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Card>> ListCardsAsync(
        Guid careProfileId, string? section, CancellationToken ct = default);
    Task RemoveCardAsync(Card card, CancellationToken ct = default);
}
