namespace CareTrack.Domain;

public enum SchoolPlanType
{
    Iep,
    Plan504,
    Other
}

/// <summary>
/// A school support plan (IEP, 504, or similar) for the school section.
/// Reuses the document pattern: the signed plan itself is an uploaded
/// <see cref="Document"/> linked via <see cref="DocumentId"/>. The review
/// date drives the upcoming-reviews view; review meetings themselves are
/// ordinary <see cref="Appointment"/>s (calendar pattern).
/// </summary>
public class SchoolPlan
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid CareProfileId { get; init; }
    public CareProfile? CareProfile { get; init; }

    public required SchoolPlanType Type { get; set; }

    /// <summary>School or district the plan is with.</summary>
    public string? School { get; set; }

    /// <summary>Optional label, e.g. "3rd grade IEP (2026–27)".</summary>
    public string? Title { get; set; }

    public DateOnly? EffectiveOn { get; set; }

    /// <summary>When the plan must next be reviewed/renewed.</summary>
    public DateOnly? ReviewDueOn { get; set; }

    /// <summary>The uploaded plan document, once one is attached.</summary>
    public Guid? DocumentId { get; set; }
    public Document? Document { get; set; }

    /// <summary>Accommodations summary, key contacts, meeting outcomes, etc.</summary>
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
