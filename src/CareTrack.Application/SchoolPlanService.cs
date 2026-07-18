using CareTrack.Domain;

namespace CareTrack.Application;

public sealed record CreateSchoolPlanRequest(
    SchoolPlanType Type, string? School, string? Title,
    DateOnly? EffectiveOn, DateOnly? ReviewDueOn, string? Notes);

/// <summary>
/// School (IEP/504) plan operations. Reuses the document pattern for the plan
/// file and surfaces upcoming review dates; review meetings are ordinary
/// appointments. Access-scoped throughout.
/// </summary>
public sealed class SchoolPlanService
{
    private readonly ICareDataRepository _repo;
    private readonly CareProfileAccessService _access;
    private readonly IClock _clock;

    public SchoolPlanService(
        ICareDataRepository repo, CareProfileAccessService access, IClock clock)
    {
        _repo = repo;
        _access = access;
        _clock = clock;
    }

    public async Task<SchoolPlan> AddAsync(
        Guid userId, Guid careProfileId, CreateSchoolPlanRequest req,
        CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Editor, ct);

        var plan = new SchoolPlan
        {
            CareProfileId = careProfileId,
            Type = req.Type,
            School = req.School,
            Title = req.Title,
            EffectiveOn = req.EffectiveOn,
            ReviewDueOn = req.ReviewDueOn,
            Notes = req.Notes
        };
        return await _repo.AddSchoolPlanAsync(plan, ct);
    }

    public async Task<IReadOnlyList<SchoolPlan>> ListAsync(
        Guid userId, Guid careProfileId, CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);
        return await _repo.ListSchoolPlansAsync(careProfileId, ct);
    }

    public async Task<SchoolPlan?> GetAsync(
        Guid userId, Guid careProfileId, Guid planId, CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);
        var plan = await _repo.GetSchoolPlanAsync(planId, ct);
        return plan?.CareProfileId == careProfileId ? plan : null;
    }

    /// <summary>
    /// Plans whose review is due within <paramref name="withinDays"/> (or
    /// already overdue), soonest first — the school section's counterpart of
    /// the follow-up queue.
    /// </summary>
    public async Task<IReadOnlyList<SchoolPlan>> ListUpcomingReviewsAsync(
        Guid userId, Guid careProfileId, int withinDays = 60,
        CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Viewer, ct);

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var horizon = today.AddDays(withinDays);
        var plans = await _repo.ListSchoolPlansAsync(careProfileId, ct);
        return plans
            .Where(p => p.ReviewDueOn is DateOnly due && due <= horizon)
            .OrderBy(p => p.ReviewDueOn)
            .ToList();
    }

    /// <summary>
    /// Attaches an uploaded document (the signed plan) to the school plan.
    /// Both must belong to the route's care profile.
    /// </summary>
    public async Task LinkDocumentAsync(
        Guid userId, Guid careProfileId, Guid planId, Guid documentId,
        CancellationToken ct = default)
    {
        await _access.RequireAccessAsync(userId, careProfileId, AccessRole.Editor, ct);

        var plan = await _repo.GetSchoolPlanAsync(planId, ct);
        if (plan is null || plan.CareProfileId != careProfileId)
            throw new AccessDeniedException();

        var document = await _repo.GetDocumentAsync(documentId, ct);
        if (document is null || document.CareProfileId != careProfileId)
            throw new AccessDeniedException();

        plan.DocumentId = documentId;
        await _repo.UpdateSchoolPlanAsync(plan, ct);
    }
}
