using CareTrack.Application;

namespace CareTrack.Infrastructure.Storage;

public sealed class LocalDiskDocumentStoreOptions
{
    public const string SectionName = "DocumentStore";
    /// <summary>Root directory under which blobs are written.</summary>
    public string RootPath { get; set; } = "App_Data/blobs";
}

/// <summary>
/// Development blob store that writes files under a local root, partitioned by
/// care profile id. Keys are "{profileId}/{guid}{ext}". Swap for an S3/Azure
/// implementation of <see cref="IDocumentStore"/> in production without touching
/// callers. Guards against path traversal by only ever composing keys itself and
/// validating keys on read/delete.
/// </summary>
public sealed class LocalDiskDocumentStore : IDocumentStore
{
    private readonly string _root;

    public LocalDiskDocumentStore(LocalDiskDocumentStoreOptions options)
    {
        _root = Path.GetFullPath(options.RootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredBlob> SaveAsync(
        Guid careProfileId, string fileName, string contentType,
        Stream content, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName);
        var key = $"{careProfileId:N}/{Guid.NewGuid():N}{ext}";
        var path = ResolveWithinRoot(key);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);

        return new StoredBlob(key, file.Length);
    }

    public Task<Stream?> OpenAsync(string storageKey, CancellationToken ct = default)
    {
        var path = ResolveWithinRoot(storageKey);
        Stream? stream = File.Exists(path)
            ? File.OpenRead(path)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var path = ResolveWithinRoot(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves a key to an absolute path and ensures it stays under the root,
    /// rejecting traversal attempts (e.g. keys containing "..").
    /// </summary>
    private string ResolveWithinRoot(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_root, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid storage key.");
        return full;
    }
}
