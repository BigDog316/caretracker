using CareTrack.Application;
using CareTrack.Domain;
using Xunit;

namespace CareTrack.Tests;

/// <summary>
/// The School (IEP/504) + Agencies slice (milestone 4), plus profile creation:
/// access is enforced everywhere, the creator of a profile becomes its Owner
/// atomically, upcoming reviews surface in date order, and cross-profile ids
/// never leak.
/// </summary>
public class SchoolAndAgencyTests
{
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _outsider = Guid.NewGuid();
    private readonly Guid _profile = Guid.NewGuid();
    private readonly Guid _otherProfile = Guid.NewGuid();

    private readonly InMemoryAccessGrantStore _grants = new();
    private readonly InMemoryCareDataRepository _repo;
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-01-15T12:00:00Z"));
    private readonly CareProfileAccessService _access;

    public SchoolAndAgencyTests()
    {
        _repo = new InMemoryCareDataRepository(_grants);
        _access = new CareProfileAccessService(_grants);
        _grants.Add(_user, _profile, AccessRole.Editor);
        _grants.Add(_user, _otherProfile, AccessRole.Viewer); // read-only elsewhere
    }

    private AgencyService Agencies() => new(_repo, _access);
    private SchoolPlanService Plans() => new(_repo, _access, _clock);
    private CareProfileService Profiles() => new(_repo);

    // ---- Profile creation ----

    [Fact]
    public async Task Creating_a_profile_makes_the_creator_its_owner()
    {
        var creator = Guid.NewGuid();
        var profile = await Profiles().CreateAsync(
            creator, new CreateCareProfileRequest("  New Kid  ", null));

        Assert.Equal("New Kid", profile.DisplayName);
        var grant = await _grants.FindActiveGrantAsync(creator, profile.Id);
        Assert.NotNull(grant);
        Assert.Equal(AccessRole.Owner, grant!.Role);

        // And the profile is immediately usable through access-scoped services.
        var agencies = await Agencies().ListAsync(creator, profile.Id);
        Assert.Empty(agencies);
    }

    [Fact]
    public async Task Blank_display_name_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Profiles().CreateAsync(_user, new CreateCareProfileRequest("   ", null)));
    }

    // ---- Agencies ----

    [Fact]
    public async Task Outsider_cannot_add_or_list_agencies()
    {
        var req = new CreateAgencyRequest(
            "Acme Insurance", AgencyKinds.Insurance, null, null, null, null, null);
        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Agencies().AddAsync(_outsider, _profile, req));
        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Agencies().ListAsync(_outsider, _profile));
    }

    [Fact]
    public async Task Viewer_cannot_add_agency()
    {
        var req = new CreateAgencyRequest(
            "County Board", AgencyKinds.CountyDdServices, null, null, null, null, null);
        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Agencies().AddAsync(_user, _otherProfile, req));
    }

    [Fact]
    public async Task Agencies_list_filters_by_kind_and_get_guards_profile()
    {
        var svc = Agencies();
        await svc.AddAsync(_user, _profile, new CreateAgencyRequest(
            "Acme Insurance", AgencyKinds.Insurance, "Casey Worker",
            "555-0100", null, null, null));
        var respite = await svc.AddAsync(_user, _profile, new CreateAgencyRequest(
            "Rest Easy Respite", AgencyKinds.Respite, null, null, null, null, null));

        var all = await svc.ListAsync(_user, _profile);
        Assert.Equal(2, all.Count);

        var onlyRespite = await svc.ListAsync(_user, _profile, AgencyKinds.Respite);
        Assert.Single(onlyRespite);
        Assert.Equal("Rest Easy Respite", onlyRespite[0].Name);

        // The agency belongs to _profile; asking for it under another profile
        // the caller can also access must not leak it.
        Assert.Null(await svc.GetAsync(_user, _otherProfile, respite.Id));
    }

    // ---- School plans ----

    private CreateSchoolPlanRequest Iep(DateOnly? reviewDueOn = null) => new(
        SchoolPlanType.Iep, "Maple Elementary", "3rd grade IEP",
        new DateOnly(2025, 9, 1), reviewDueOn, null);

    [Fact]
    public async Task Viewer_can_read_but_not_add_school_plans()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Plans().AddAsync(_user, _otherProfile, Iep()));

        var listed = await Plans().ListAsync(_user, _otherProfile);
        Assert.Empty(listed);
    }

    [Fact]
    public async Task Upcoming_reviews_are_windowed_and_sorted()
    {
        var svc = Plans();
        // Today (per FixedClock) is 2026-01-15.
        await svc.AddAsync(_user, _profile, Iep(new DateOnly(2026, 3, 1)));   // in 45d
        await svc.AddAsync(_user, _profile, Iep(new DateOnly(2026, 1, 1)));   // overdue
        await svc.AddAsync(_user, _profile, Iep(new DateOnly(2026, 9, 1)));   // far out
        await svc.AddAsync(_user, _profile, Iep(reviewDueOn: null));          // no date

        var upcoming = await svc.ListUpcomingReviewsAsync(_user, _profile, withinDays: 60);

        Assert.Equal(2, upcoming.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), upcoming[0].ReviewDueOn); // overdue first
        Assert.Equal(new DateOnly(2026, 3, 1), upcoming[1].ReviewDueOn);
    }

    [Fact]
    public async Task Linking_a_document_from_another_profile_is_denied()
    {
        var plan = await Plans().AddAsync(_user, _profile, Iep());
        var foreignDoc = new Document
        {
            CareProfileId = _otherProfile,
            FileName = "iep.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1,
            StorageKey = "k",
            UploadedByUserId = _user
        };
        await _repo.AddDocumentAsync(foreignDoc);

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            Plans().LinkDocumentAsync(_user, _profile, plan.Id, foreignDoc.Id));
        Assert.Null(plan.DocumentId);
    }

    [Fact]
    public async Task Linking_a_document_in_the_same_profile_succeeds()
    {
        var plan = await Plans().AddAsync(_user, _profile, Iep());
        var doc = new Document
        {
            CareProfileId = _profile,
            FileName = "iep-signed.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1,
            StorageKey = "k",
            UploadedByUserId = _user
        };
        await _repo.AddDocumentAsync(doc);

        await Plans().LinkDocumentAsync(_user, _profile, plan.Id, doc.Id);
        Assert.Equal(doc.Id, plan.DocumentId);
    }
}
