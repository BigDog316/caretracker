namespace CareTrack.Domain;

/// <summary>A free-form tag attached to a document for filtering/search.</summary>
public class DocumentTag
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid DocumentId { get; init; }
    public Document? Document { get; init; }

    public required string Value { get; set; }
}
