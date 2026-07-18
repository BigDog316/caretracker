namespace CareTrack.Domain;

/// <summary>
/// A care provider: doctor, therapist, specialist, etc. Belongs to a single
/// <see cref="CareProfile"/> and is reachable only by users with a grant to it.
/// </summary>
public class Provider
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid CareProfileId { get; init; }
    public CareProfile? CareProfile { get; init; }

    public required string Name { get; set; }
    public string? Organization { get; set; }
    public string? Specialty { get; set; }

    /// <summary>Free-form address; the client deep-links this to a map app.</summary>
    public string? Address { get; set; }

    /// <summary>Phone in a dialable form; the client deep-links via tel:.</summary>
    public string? Phone { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public ICollection<Appointment> Appointments { get; } = new List<Appointment>();
    public ICollection<Note> Notes { get; } = new List<Note>();
}
