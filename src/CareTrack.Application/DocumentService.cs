using CareTrack.Domain;

namespace CareTrack.Application;

public sealed record UploadDocumentRequest(
    string FileName, string ContentType, string? Description,
    IReadOnlyCollection<string> Tags, Guid? ProviderId, Guid? AppointmentId);

public sealed record DocumentDownload(
    string FileName, string ContentType, Stream Content);

/// <summary>
/// Uploads, lists, downloads, and deletes documents. Coordinates the blob store
/// (file bytes) and the repository (metadata). Access-scoped on every operation.
/// </summary>
public sealed class DocumentService
{
    private readonly ICareDataRepository _repo;
    private readonly CareProfileAccessService _access;
    private readonly IDocumentStore _store;

    public DocumentService(
        ICareDataRepository repo, CareProfileAccessService access, IDocumentStore store)
    {
        _repo = repo;
        _access = access;
        _store = store;
    }

    public async Task<Document> UploadAsync(
        Guid userId, Guid careProfileId, UploadDocumentRequest req, Stream content,
        CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Editor, ct);

        var blob = await _store.SaveAsync(
            careProfileId, req.FileName, req.ContentType, content, ct);

        var document = new Document
        {
            CareProfileId = careProfileId,
            FileName = req.FileName,
            ContentType = req.ContentType,
            SizeBytes = blob.SizeBytes,
            StorageKey = blob.StorageKey,
            Description = req.Description,
            ProviderId = req.ProviderId,
            AppointmentId = req.AppointmentId,
            UploadedByUserId = userId
        };

        foreach (var tag in req.Tags.Where(t => !string.IsNullOrWhiteSpace(t)))
            document.Tags.Add(new DocumentTag
            {
                DocumentId = document.Id,
                Value = tag.Trim()
            });

        return await _repo.AddDocumentAsync(document, ct);
    }

    public async Task<IReadOnlyList<Document>> ListAsync(
        Guid userId, Guid careProfileId, string? tag, CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);
        return await _repo.ListDocumentsAsync(careProfileId, tag, ct);
    }

    public async Task<DocumentDownload?> DownloadAsync(
        Guid userId, Guid careProfileId, Guid documentId, CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);

        var doc = await _repo.GetDocumentAsync(documentId, ct);
        if (doc is null || doc.CareProfileId != careProfileId)
            return null;

        var stream = await _store.OpenAsync(doc.StorageKey, ct);
        return stream is null ? null
            : new DocumentDownload(doc.FileName, doc.ContentType, stream);
    }

    public async Task DeleteAsync(
        Guid userId, Guid careProfileId, Guid documentId, CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Editor, ct);

        var doc = await _repo.GetDocumentAsync(documentId, ct);
        if (doc is null || doc.CareProfileId != careProfileId)
            throw new AccessDeniedException();

        await _repo.RemoveDocumentAsync(doc, ct);
        await _store.DeleteAsync(doc.StorageKey, ct);
    }
}
