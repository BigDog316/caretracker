using CareTrack.Domain;

namespace CareTrack.Application;

public sealed record CreateNoteRequest(
    string Body, Guid? AppointmentId, Guid? ProviderId);

/// <summary>
/// Note operations: create (standalone, or attached to an appointment/provider),
/// keyword search across a profile, and provider-filtered history for the
/// view/print/download feature. Access-scoped throughout.
/// </summary>
public sealed class NoteService
{
    private readonly ICareDataRepository _repo;
    private readonly CareProfileAccessService _access;

    public NoteService(ICareDataRepository repo, CareProfileAccessService access)
    {
        _repo = repo;
        _access = access;
    }

    public async Task<Note> AddAsync(
        Guid userId, Guid careProfileId, CreateNoteRequest req,
        CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Editor, ct);

        if (string.IsNullOrWhiteSpace(req.Body))
            throw new ArgumentException("Note body must not be empty.");

        var note = new Note
        {
            CareProfileId = careProfileId,
            AppointmentId = req.AppointmentId,
            ProviderId = req.ProviderId,
            Body = req.Body.Trim(),
            AuthorUserId = userId
        };
        return await _repo.AddNoteAsync(note, ct);
    }

    public async Task<IReadOnlyList<Note>> SearchAsync(
        Guid userId, Guid careProfileId, string keyword, CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);
        return string.IsNullOrWhiteSpace(keyword)
            ? await _repo.ListNotesAsync(careProfileId, ct)
            : await _repo.SearchNotesAsync(careProfileId, keyword.Trim(), ct);
    }

    /// <summary>
    /// History across one or several providers, for the view/print/download
    /// feature. An empty provider set returns the full profile history.
    /// </summary>
    public async Task<IReadOnlyList<Note>> ProviderHistoryAsync(
        Guid userId, Guid careProfileId, IReadOnlyCollection<Guid> providerIds,
        CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);
        return providerIds.Count == 0
            ? await _repo.ListNotesAsync(careProfileId, ct)
            : await _repo.ListNotesForProvidersAsync(careProfileId, providerIds, ct);
    }
}
