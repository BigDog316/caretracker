using CareTrack.Application;
using CareTrack.Domain;
using Xunit;

namespace CareTrack.Tests;

/// <summary>In-memory blob store for tests.</summary>
internal sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly Dictionary<string, byte[]> _blobs = new();

    public async Task<StoredBlob> SaveAsync(
        Guid careProfileId, string fileName, string contentType,
        Stream content, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var key = $"{careProfileId:N}/{Guid.NewGuid():N}";
        _blobs[key] = ms.ToArray();
        return new StoredBlob(key, _blobs[key].Length);
    }

    public Task<Stream?> OpenAsync(string storageKey, CancellationToken ct = default)
        => Task.FromResult<Stream?>(
            _blobs.TryGetValue(storageKey, out var bytes)
                ? new MemoryStream(bytes)
                : null);

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    { _blobs.Remove(storageKey); return Task.CompletedTask; }

    public bool Exists(string key) => _blobs.ContainsKey(key);
}

public class DocumentAndCardTests
{
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _outsider = Guid.NewGuid();
    private readonly Guid _profile = Guid.NewGuid();

    private readonly InMemoryAccessGrantStore _grants = new();
    private readonly InMemoryCareDataRepository _repo = new();
    private readonly InMemoryDocumentStore _store = new();
    private readonly CareProfileAccessService _access;

    public DocumentAndCardTests()
    {
        _access = new CareProfileAccessService(_grants);
        _grants.Add(_user, _profile, AccessRole.Editor);
    }

    private static Stream Bytes(string s) =>
        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(s));

    [Fact]
    public async Task Upload_stores_blob_and_metadata_with_tags()
    {
        var svc = new DocumentService(_repo, _access, _store);
        var doc = await svc.UploadAsync(_user, _profile,
            new UploadDocumentRequest("iep.pdf", "application/pdf", "2026 IEP",
                new[] { "IEP", "2026" }, null, null),
            Bytes("file-content"));

        Assert.Equal("iep.pdf", doc.FileName);
        Assert.True(doc.SizeBytes > 0);
        Assert.True(_store.Exists(doc.StorageKey));
        Assert.Equal(2, doc.Tags.Count);
    }

    [Fact]
    public async Task Outsider_cannot_upload()
    {
        var svc = new DocumentService(_repo, _access, _store);
        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            svc.UploadAsync(_outsider, _profile,
                new UploadDocumentRequest("x.pdf", "application/pdf", null,
                    Array.Empty<string>(), null, null),
                Bytes("x")));
    }

    [Fact]
    public async Task Documents_filter_by_tag()
    {
        var svc = new DocumentService(_repo, _access, _store);
        await svc.UploadAsync(_user, _profile,
            new UploadDocumentRequest("a.pdf", "application/pdf", null,
                new[] { "IEP" }, null, null), Bytes("a"));
        await svc.UploadAsync(_user, _profile,
            new UploadDocumentRequest("b.pdf", "application/pdf", null,
                new[] { "ReportCard" }, null, null), Bytes("b"));

        var ieps = await svc.ListAsync(_user, _profile, "IEP");
        Assert.Single(ieps);
        Assert.Equal("a.pdf", ieps[0].FileName);
    }

    [Fact]
    public async Task Delete_removes_metadata_and_blob()
    {
        var svc = new DocumentService(_repo, _access, _store);
        var doc = await svc.UploadAsync(_user, _profile,
            new UploadDocumentRequest("d.pdf", "application/pdf", null,
                Array.Empty<string>(), null, null), Bytes("d"));

        Assert.True(_store.Exists(doc.StorageKey));
        await svc.DeleteAsync(_user, _profile, doc.Id);
        Assert.False(_store.Exists(doc.StorageKey));
        Assert.Empty(await svc.ListAsync(_user, _profile, null));
    }

    [Fact]
    public async Task Card_add_and_filter_by_section()
    {
        var svc = new CardService(_repo, _access, _store);
        await svc.AddAsync(_user, _profile,
            new AddCardRequest(CardSections.Insurance, "Medicaid", "image/png", null),
            Bytes("img1"));
        await svc.AddAsync(_user, _profile,
            new AddCardRequest(CardSections.Membership, "Zoo", "image/png", null),
            Bytes("img2"));

        var insurance = await svc.ListAsync(_user, _profile, CardSections.Insurance);
        Assert.Single(insurance);
        Assert.Equal("Medicaid", insurance[0].Label);
    }

    [Fact]
    public async Task Card_requires_section()
    {
        var svc = new CardService(_repo, _access, _store);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.AddAsync(_user, _profile,
                new AddCardRequest("  ", null, "image/png", null), Bytes("x")));
    }

    [Fact]
    public async Task Card_image_download_returns_stored_bytes()
    {
        var svc = new CardService(_repo, _access, _store);
        var card = await svc.AddAsync(_user, _profile,
            new AddCardRequest(CardSections.Other, null, "image/png", null),
            Bytes("hello"));

        var img = await svc.ImageAsync(_user, _profile, card.Id);
        Assert.NotNull(img);
        using var reader = new StreamReader(img!.Content);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }
}
