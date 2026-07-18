namespace CareTrack.Domain;

/// <summary>
/// Metadata for an uploaded file (IEP, evaluation, report card, scan, etc.).
/// The file bytes live in blob storage (see IDocumentStore); this row holds only
/// the pointer and descriptive metadata. Belongs to a care profile and may
/// optionally be associated with a provider or appointment.
/// </summary>
public class Document
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid CareProfileId { get; init; }
    public CareProfile? CareProfile { get; init; }

    public Guid? ProviderId { get; set; }
    public Guid? AppointmentId { get; set; }

    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public required long SizeBytes { get; init; }

    /// <summary>Opaque key identifying the object in the blob store.</summary>
    public required string StorageKey { get; init; }

    /// <summary>Optional accessibility description / caption for the file.</summary>
    public string? Description { get; set; }

    public required Guid UploadedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public ICollection<DocumentTag> Tags { get; } = new List<DocumentTag>();
}
