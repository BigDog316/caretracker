namespace CareTrack.Domain;

/// <summary>
/// A photographed physical card (insurance/Medicaid, provider business card,
/// appointment reminder, membership, etc.). Like a document, the image lives in
/// blob storage; this row holds the pointer, the section, and metadata.
/// Sections are user-definable strings with sensible defaults rather than a
/// closed enum, so caregivers can add their own categories.
/// </summary>
public class Card
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid CareProfileId { get; init; }
    public CareProfile? CareProfile { get; init; }

    /// <summary>User-facing category, e.g. "Insurance", "Membership".</summary>
    public required string Section { get; set; }

    public string? Label { get; set; }

    public required string ContentType { get; set; }
    public required long SizeBytes { get; init; }
    public required string StorageKey { get; init; }

    /// <summary>Accessibility description of the card image.</summary>
    public string? Description { get; set; }

    public required Guid UploadedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Suggested default card sections offered in the UI.</summary>
public static class CardSections
{
    public const string Insurance = "Insurance";
    public const string ProviderBusinessCard = "Provider Business Card";
    public const string AppointmentReminder = "Appointment Reminder";
    public const string Membership = "Membership";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> Defaults = new[]
    {
        Insurance, ProviderBusinessCard, AppointmentReminder, Membership, Other
    };
}
