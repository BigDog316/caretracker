namespace CareTrack.Domain;

/// <summary>
/// A free-text note. Always belongs to a care profile; may additionally attach
/// to an appointment (e.g. the "How did it go?" response) or a provider, or
/// stand alone. Notes are keyword-searchable across a profile.
/// </summary>
public class Note
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid CareProfileId { get; init; }
    public CareProfile? CareProfile { get; init; }

    /// <summary>Optional: the appointment this note is about.</summary>
    public Guid? AppointmentId { get; set; }
    public Appointment? Appointment { get; init; }

    /// <summary>Optional: the provider this note is about.</summary>
    public Guid? ProviderId { get; set; }
    public Provider? Provider { get; init; }

    public required string Body { get; set; }

    /// <summary>Id of the user who authored the note (for audit/attribution).</summary>
    public required Guid AuthorUserId { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
