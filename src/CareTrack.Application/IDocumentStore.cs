namespace CareTrack.Application;

/// <summary>Result of storing a blob.</summary>
public sealed record StoredBlob(string StorageKey, long SizeBytes);

/// <summary>
/// Abstraction over blob storage for uploaded documents and card images. A
/// local-disk implementation backs development; an S3/Azure Blob implementation
/// backs production. Files are never stored in the relational database — only
/// their metadata rows, which reference the returned <see cref="StoredBlob.StorageKey"/>.
/// </summary>
public interface IDocumentStore
{
    /// <summary>Stores a blob for a profile and returns its storage key + size.</summary>
    Task<StoredBlob> SaveAsync(
        Guid careProfileId, string fileName, string contentType,
        Stream content, CancellationToken ct = default);

    /// <summary>Opens the stored blob for reading, or null if it is missing.</summary>
    Task<Stream?> OpenAsync(string storageKey, CancellationToken ct = default);

    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}
