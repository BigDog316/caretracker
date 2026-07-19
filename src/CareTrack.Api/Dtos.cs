using CareTrack.Domain;

namespace CareTrack.Api;

/// <summary>
/// Response DTOs. Controllers previously serialized EF entities directly,
/// which leaked navigation properties and produced object cycles once a
/// bidirectional navigation was loaded (Document ↔ DocumentTag). Field names
/// mirror what the Blazor client already binds to.
/// </summary>
public static class Dtos
{
    public sealed record ProviderDto(
        Guid Id, string Name, string? Organization, string? Specialty,
        string? Address, string? Phone, DateTimeOffset CreatedAt);

    public sealed record AppointmentDto(
        Guid Id, Guid CareProfileId, string Title, string? Location,
        DateTimeOffset StartsAt, DateTimeOffset EndsAt, Guid? ProviderId,
        DateTimeOffset? FollowUpCompletedAt, string? ExternalCalendarEventId,
        DateTimeOffset CreatedAt);

    public sealed record NoteDto(
        Guid Id, Guid CareProfileId, string Body, Guid? AppointmentId,
        Guid? ProviderId, Guid AuthorUserId, DateTimeOffset CreatedAt);

    public sealed record DocumentTagDto(string Value);

    public sealed record DocumentDto(
        Guid Id, string FileName, string ContentType, long SizeBytes,
        string? Description, Guid? ProviderId, Guid? AppointmentId,
        DateTimeOffset CreatedAt, IReadOnlyList<DocumentTagDto> Tags);

    public sealed record CardDto(
        Guid Id, string Section, string? Label, string ContentType,
        long SizeBytes, string? Description, DateTimeOffset CreatedAt);

    public sealed record SchoolPlanDto(
        Guid Id, Guid CareProfileId, SchoolPlanType Type, string? School,
        string? Title, DateOnly? EffectiveOn, DateOnly? ReviewDueOn,
        Guid? DocumentId, string? Notes, DateTimeOffset CreatedAt);

    public sealed record AgencyDto(
        Guid Id, string Name, string Kind, string? ContactName, string? Phone,
        string? Email, string? Address, string? Notes, DateTimeOffset CreatedAt);

    public static ProviderDto ToDto(this Provider p) => new(
        p.Id, p.Name, p.Organization, p.Specialty, p.Address, p.Phone, p.CreatedAt);

    public static AppointmentDto ToDto(this Appointment a) => new(
        a.Id, a.CareProfileId, a.Title, a.Location, a.StartsAt, a.EndsAt,
        a.ProviderId, a.FollowUpCompletedAt, a.ExternalCalendarEventId, a.CreatedAt);

    public static NoteDto ToDto(this Note n) => new(
        n.Id, n.CareProfileId, n.Body, n.AppointmentId, n.ProviderId,
        n.AuthorUserId, n.CreatedAt);

    public static DocumentDto ToDto(this Document d) => new(
        d.Id, d.FileName, d.ContentType, d.SizeBytes, d.Description,
        d.ProviderId, d.AppointmentId, d.CreatedAt,
        d.Tags.Select(t => new DocumentTagDto(t.Value)).ToList());

    public static CardDto ToDto(this Card c) => new(
        c.Id, c.Section, c.Label, c.ContentType, c.SizeBytes, c.Description,
        c.CreatedAt);

    public static SchoolPlanDto ToDto(this SchoolPlan p) => new(
        p.Id, p.CareProfileId, p.Type, p.School, p.Title, p.EffectiveOn,
        p.ReviewDueOn, p.DocumentId, p.Notes, p.CreatedAt);

    public static AgencyDto ToDto(this Agency a) => new(
        a.Id, a.Name, a.Kind, a.ContactName, a.Phone, a.Email, a.Address,
        a.Notes, a.CreatedAt);

    public static IReadOnlyList<TOut> ToDtos<TIn, TOut>(
        this IReadOnlyList<TIn> items, Func<TIn, TOut> map)
        => items.Select(map).ToList();
}
