using CareTrack.Domain;

namespace CareTrack.Application;

public sealed record AddCardRequest(
    string Section, string? Label, string ContentType, string? Description);

public sealed record CardDownload(string ContentType, Stream Content);

/// <summary>
/// Cards are photographed physical cards organized by section. Same storage +
/// metadata coordination as documents. Access-scoped throughout.
/// </summary>
public sealed class CardService
{
    private readonly ICareDataRepository _repo;
    private readonly CareProfileAccessService _access;
    private readonly IDocumentStore _store;

    public CardService(
        ICareDataRepository repo, CareProfileAccessService access, IDocumentStore store)
    {
        _repo = repo;
        _access = access;
        _store = store;
    }

    /// <summary>Default sections offered to the user when adding a card.</summary>
    public IReadOnlyList<string> DefaultSections => CardSections.Defaults;

    public async Task<Card> AddAsync(
        Guid userId, Guid careProfileId, AddCardRequest req, Stream image,
        CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Editor, ct);

        if (string.IsNullOrWhiteSpace(req.Section))
            throw new ArgumentException("A card section is required.");

        var fileName = $"card{GuessExtension(req.ContentType)}";
        var blob = await _store.SaveAsync(
            careProfileId, fileName, req.ContentType, image, ct);

        var card = new Card
        {
            CareProfileId = careProfileId,
            Section = req.Section.Trim(),
            Label = req.Label,
            ContentType = req.ContentType,
            SizeBytes = blob.SizeBytes,
            StorageKey = blob.StorageKey,
            Description = req.Description,
            UploadedByUserId = userId
        };
        return await _repo.AddCardAsync(card, ct);
    }

    public async Task<IReadOnlyList<Card>> ListAsync(
        Guid userId, Guid careProfileId, string? section, CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);
        return await _repo.ListCardsAsync(careProfileId, section, ct);
    }

    public async Task<CardDownload?> ImageAsync(
        Guid userId, Guid careProfileId, Guid cardId, CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);

        var card = await _repo.GetCardAsync(cardId, ct);
        if (card is null || card.CareProfileId != careProfileId)
            return null;

        var stream = await _store.OpenAsync(card.StorageKey, ct);
        return stream is null ? null : new CardDownload(card.ContentType, stream);
    }

    public async Task DeleteAsync(
        Guid userId, Guid careProfileId, Guid cardId, CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Editor, ct);

        var card = await _repo.GetCardAsync(cardId, ct);
        if (card is null || card.CareProfileId != careProfileId)
            throw new AccessDeniedException();

        await _repo.RemoveCardAsync(card, ct);
        await _store.DeleteAsync(card.StorageKey, ct);
    }

    private static string GuessExtension(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/heic" => ".heic",
        "image/webp" => ".webp",
        _ => string.Empty
    };
}
