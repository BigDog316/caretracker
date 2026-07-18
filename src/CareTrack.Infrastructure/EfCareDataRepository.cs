using CareTrack.Application;
using CareTrack.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareTrack.Infrastructure;

public sealed class EfCareDataRepository : ICareDataRepository
{
    private readonly CareTrackDbContext _db;

    public EfCareDataRepository(CareTrackDbContext db) => _db = db;

    // ---- Care profiles ----

    public async Task CreateCareProfileAsync(
        CareProfile profile, AccessGrant ownerGrant, CancellationToken ct = default)
    {
        // One SaveChanges = one transaction: the profile and its owning grant
        // are created atomically.
        _db.CareProfiles.Add(profile);
        _db.AccessGrants.Add(ownerGrant);
        await _db.SaveChangesAsync(ct);
    }

    // ---- Providers ----

    public async Task<Provider> AddProviderAsync(Provider provider, CancellationToken ct = default)
    {
        _db.Providers.Add(provider);
        await _db.SaveChangesAsync(ct);
        return provider;
    }

    public Task<Provider?> GetProviderAsync(Guid id, CancellationToken ct = default)
        => _db.Providers.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Provider>> ListProvidersAsync(
        Guid careProfileId, CancellationToken ct = default)
        => await _db.Providers.AsNoTracking()
            .Where(p => p.CareProfileId == careProfileId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    // ---- Appointments ----

    public async Task<Appointment> AddAppointmentAsync(Appointment appt, CancellationToken ct = default)
    {
        _db.Appointments.Add(appt);
        await _db.SaveChangesAsync(ct);
        return appt;
    }

    public Task<Appointment?> GetAppointmentAsync(Guid id, CancellationToken ct = default)
        => _db.Appointments.SingleOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<Appointment>> ListAppointmentsAsync(
        Guid careProfileId, CancellationToken ct = default)
        => await _db.Appointments.AsNoTracking()
            .Where(a => a.CareProfileId == careProfileId)
            .OrderByDescending(a => a.StartsAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Appointment>> ListAwaitingFollowUpAsync(
        IEnumerable<Guid> careProfileIds, DateTimeOffset now, CancellationToken ct = default)
    {
        var ids = careProfileIds.ToArray();
        return await _db.Appointments.AsNoTracking()
            .Where(a => ids.Contains(a.CareProfileId)
                        && a.FollowUpCompletedAt == null
                        && a.EndsAt <= now)
            .OrderByDescending(a => a.EndsAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Appointment>> ListDueForFollowUpReminderAsync(
        DateTimeOffset now, TimeSpan repromptAfter, CancellationToken ct = default)
    {
        var repromptBefore = now - repromptAfter;
        return await _db.Appointments
            .Include(a => a.CareProfile)
            .Where(a => a.FollowUpCompletedAt == null
                        && a.EndsAt <= now
                        && (a.FollowUpLastRemindedAt == null
                            || a.FollowUpLastRemindedAt <= repromptBefore))
            .OrderBy(a => a.EndsAt)
            .ToListAsync(ct);
    }

    public async Task UpdateAppointmentAsync(Appointment appt, CancellationToken ct = default)
    {
        _db.Appointments.Update(appt);
        await _db.SaveChangesAsync(ct);
    }

    // ---- Notes ----

    public async Task<Note> AddNoteAsync(Note note, CancellationToken ct = default)
    {
        _db.Notes.Add(note);
        await _db.SaveChangesAsync(ct);
        return note;
    }

    public async Task<IReadOnlyList<Note>> ListNotesAsync(
        Guid careProfileId, CancellationToken ct = default)
        => await _db.Notes.AsNoTracking()
            .Where(n => n.CareProfileId == careProfileId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Note>> SearchNotesAsync(
        Guid careProfileId, string keyword, CancellationToken ct = default)
        => await _db.Notes.AsNoTracking()
            .Where(n => n.CareProfileId == careProfileId
                        && EF.Functions.ToTsVector("english", n.Body)
                            .Matches(EF.Functions.PlainToTsQuery("english", keyword)))
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Note>> ListNotesForProvidersAsync(
        Guid careProfileId, IReadOnlyCollection<Guid> providerIds, CancellationToken ct = default)
    {
        var ids = providerIds.ToArray();
        return await _db.Notes.AsNoTracking()
            .Where(n => n.CareProfileId == careProfileId
                        && n.ProviderId != null
                        && ids.Contains(n.ProviderId!.Value))
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);
    }

    // ---- Agencies ----

    public async Task<Agency> AddAgencyAsync(Agency agency, CancellationToken ct = default)
    {
        _db.Agencies.Add(agency);
        await _db.SaveChangesAsync(ct);
        return agency;
    }

    public Task<Agency?> GetAgencyAsync(Guid id, CancellationToken ct = default)
        => _db.Agencies.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<Agency>> ListAgenciesAsync(
        Guid careProfileId, string? kind, CancellationToken ct = default)
        => await _db.Agencies.AsNoTracking()
            .Where(a => a.CareProfileId == careProfileId
                        && (string.IsNullOrEmpty(kind) || a.Kind == kind))
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

    // ---- School plans ----

    public async Task<SchoolPlan> AddSchoolPlanAsync(SchoolPlan plan, CancellationToken ct = default)
    {
        _db.SchoolPlans.Add(plan);
        await _db.SaveChangesAsync(ct);
        return plan;
    }

    public Task<SchoolPlan?> GetSchoolPlanAsync(Guid id, CancellationToken ct = default)
        => _db.SchoolPlans.SingleOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<SchoolPlan>> ListSchoolPlansAsync(
        Guid careProfileId, CancellationToken ct = default)
        => await _db.SchoolPlans.AsNoTracking()
            .Where(p => p.CareProfileId == careProfileId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task UpdateSchoolPlanAsync(SchoolPlan plan, CancellationToken ct = default)
    {
        _db.SchoolPlans.Update(plan);
        await _db.SaveChangesAsync(ct);
    }

    // ---- Documents ----

    public async Task<Document> AddDocumentAsync(Document document, CancellationToken ct = default)
    {
        _db.Documents.Add(document);
        await _db.SaveChangesAsync(ct);
        return document;
    }

    public Task<Document?> GetDocumentAsync(Guid id, CancellationToken ct = default)
        => _db.Documents.AsNoTracking()
            .Include(d => d.Tags)
            .SingleOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<Document>> ListDocumentsAsync(
        Guid careProfileId, string? tag, CancellationToken ct = default)
    {
        var q = _db.Documents.AsNoTracking()
            .Include(d => d.Tags)
            .Where(d => d.CareProfileId == careProfileId);

        if (!string.IsNullOrWhiteSpace(tag))
            q = q.Where(d => d.Tags.Any(t => t.Value == tag));

        return await q.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
    }

    public async Task RemoveDocumentAsync(Document document, CancellationToken ct = default)
    {
        _db.Documents.Remove(document);
        await _db.SaveChangesAsync(ct);
    }

    // ---- Cards ----

    public async Task<Card> AddCardAsync(Card card, CancellationToken ct = default)
    {
        _db.Cards.Add(card);
        await _db.SaveChangesAsync(ct);
        return card;
    }

    public Task<Card?> GetCardAsync(Guid id, CancellationToken ct = default)
        => _db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Card>> ListCardsAsync(
        Guid careProfileId, string? section, CancellationToken ct = default)
    {
        var q = _db.Cards.AsNoTracking()
            .Where(c => c.CareProfileId == careProfileId);

        if (!string.IsNullOrWhiteSpace(section))
            q = q.Where(c => c.Section == section);

        return await q.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
    }

    public async Task RemoveCardAsync(Card card, CancellationToken ct = default)
    {
        _db.Cards.Remove(card);
        await _db.SaveChangesAsync(ct);
    }
}
