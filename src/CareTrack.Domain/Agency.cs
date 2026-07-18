namespace CareTrack.Domain;

/// <summary>
/// An external organization involved in care: insurance, county board / DD
/// services, Medicaid waiver program, respite provider, transportation, etc.
/// Belongs to a single <see cref="CareProfile"/>. Like card sections, kinds are
/// user-definable strings with suggested defaults rather than a closed enum.
/// </summary>
public class Agency
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid CareProfileId { get; init; }
    public CareProfile? CareProfile { get; init; }

    public required string Name { get; set; }

    /// <summary>User-facing category, e.g. "Insurance", "Respite".</summary>
    public required string Kind { get; set; }

    /// <summary>The person to ask for, e.g. a case worker or coordinator.</summary>
    public string? ContactName { get; set; }

    /// <summary>Phone in a dialable form; the client deep-links via tel:.</summary>
    public string? Phone { get; set; }

    public string? Email { get; set; }

    /// <summary>Free-form address; the client deep-links this to a map app.</summary>
    public string? Address { get; set; }

    /// <summary>Case numbers, portal URLs, renewal quirks, etc.</summary>
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Suggested default agency kinds offered in the UI.</summary>
public static class AgencyKinds
{
    public const string Insurance = "Insurance";
    public const string CountyDdServices = "County / DD Services";
    public const string MedicaidWaiver = "Medicaid / Waiver";
    public const string Respite = "Respite";
    public const string Transportation = "Transportation";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> Defaults = new[]
    {
        Insurance, CountyDdServices, MedicaidWaiver, Respite, Transportation, Other
    };
}
