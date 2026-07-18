using CareTrack.Application;
using CareTrack.Domain;
using Xunit;

namespace CareTrack.Tests;

/// <summary>
/// Proves the security spine: a user can only reach care profiles they hold an
/// active grant for, at the appropriate role. These tests encode the sharing
/// scenario from the requirements and the negative cases that must never pass.
/// </summary>
public class CareProfileAccessTests
{
    // The requirements scenario:
    //   Caregiver A -> ChildA, ChildB, AdultC
    //   Caregiver B -> ChildA, ChildD
    //   Grandmother -> AdultC
    private readonly Guid _caregiverA = Guid.NewGuid();
    private readonly Guid _caregiverB = Guid.NewGuid();
    private readonly Guid _grandmother = Guid.NewGuid();

    private readonly Guid _childA = Guid.NewGuid();
    private readonly Guid _childB = Guid.NewGuid();
    private readonly Guid _childD = Guid.NewGuid();
    private readonly Guid _adultC = Guid.NewGuid();

    private CareProfileAccessService BuildScenario(InMemoryAccessGrantStore store)
    {
        store.Add(_caregiverA, _childA, AccessRole.Owner);
        store.Add(_caregiverA, _childB, AccessRole.Owner);
        store.Add(_caregiverA, _adultC, AccessRole.Editor);

        store.Add(_caregiverB, _childA, AccessRole.Viewer);
        store.Add(_caregiverB, _childD, AccessRole.Owner);

        store.Add(_grandmother, _adultC, AccessRole.Viewer);

        return new CareProfileAccessService(store);
    }

    [Fact]
    public async Task User_with_grant_has_access()
    {
        var svc = BuildScenario(new InMemoryAccessGrantStore());
        Assert.True(await svc.HasAccessAsync(_caregiverA, _childA));
        Assert.True(await svc.HasAccessAsync(_grandmother, _adultC));
    }

    [Fact]
    public async Task User_without_grant_is_denied()
    {
        var svc = BuildScenario(new InMemoryAccessGrantStore());

        // Grandmother may see AdultC only — never any of the children.
        Assert.False(await svc.HasAccessAsync(_grandmother, _childA));
        Assert.False(await svc.HasAccessAsync(_grandmother, _childB));
        Assert.False(await svc.HasAccessAsync(_grandmother, _childD));

        // Caregiver B never granted AdultC or ChildB.
        Assert.False(await svc.HasAccessAsync(_caregiverB, _adultC));
        Assert.False(await svc.HasAccessAsync(_caregiverB, _childB));
    }

    [Fact]
    public async Task RequireAccess_throws_for_unshared_profile()
    {
        var svc = BuildScenario(new InMemoryAccessGrantStore());
        await Assert.ThrowsAsync<AccessDeniedException>(
            () => svc.RequireAccessAsync(_caregiverB, _childB));
    }

    [Fact]
    public async Task RequireAccess_succeeds_for_shared_profile()
    {
        var svc = BuildScenario(new InMemoryAccessGrantStore());
        // Does not throw.
        await svc.RequireAccessAsync(_caregiverA, _childB, AccessRole.Owner);
    }

    [Fact]
    public async Task Viewer_cannot_meet_editor_requirement()
    {
        var svc = BuildScenario(new InMemoryAccessGrantStore());

        // Grandmother is a Viewer on AdultC; an edit action needs Editor.
        Assert.True(await svc.HasAccessAsync(_grandmother, _adultC, AccessRole.Viewer));
        Assert.False(await svc.HasAccessAsync(_grandmother, _adultC, AccessRole.Editor));
        await Assert.ThrowsAsync<AccessDeniedException>(
            () => svc.RequireAccessAsync(_grandmother, _adultC, AccessRole.Editor));
    }

    [Fact]
    public async Task Owner_satisfies_lower_role_requirements()
    {
        var svc = BuildScenario(new InMemoryAccessGrantStore());

        // Caregiver A is Owner of ChildA; Owner >= Editor >= Viewer.
        Assert.True(await svc.HasAccessAsync(_caregiverA, _childA, AccessRole.Viewer));
        Assert.True(await svc.HasAccessAsync(_caregiverA, _childA, AccessRole.Editor));
        Assert.True(await svc.HasAccessAsync(_caregiverA, _childA, AccessRole.Owner));
    }

    [Fact]
    public async Task Revoked_grant_denies_access()
    {
        var store = new InMemoryAccessGrantStore();
        store.Add(_caregiverA, _childA, AccessRole.Owner, revoked: true);
        var svc = new CareProfileAccessService(store);

        Assert.False(await svc.HasAccessAsync(_caregiverA, _childA));
        await Assert.ThrowsAsync<AccessDeniedException>(
            () => svc.RequireAccessAsync(_caregiverA, _childA));
    }

    [Fact]
    public async Task Accessible_profile_list_is_scoped_per_user()
    {
        var svc = BuildScenario(new InMemoryAccessGrantStore());

        var aIds = await svc.AccessibleProfileIdsAsync(_caregiverA);
        Assert.Equal(3, aIds.Count);
        Assert.Contains(_childA, aIds);
        Assert.Contains(_childB, aIds);
        Assert.Contains(_adultC, aIds);
        Assert.DoesNotContain(_childD, aIds);

        var gIds = await svc.AccessibleProfileIdsAsync(_grandmother);
        Assert.Single(gIds);
        Assert.Contains(_adultC, gIds);
    }

    [Fact]
    public async Task Revoked_grant_excluded_from_accessible_list()
    {
        var store = new InMemoryAccessGrantStore();
        store.Add(_caregiverA, _childA, AccessRole.Owner);
        store.Add(_caregiverA, _childB, AccessRole.Owner, revoked: true);
        var svc = new CareProfileAccessService(store);

        var ids = await svc.AccessibleProfileIdsAsync(_caregiverA);
        Assert.Single(ids);
        Assert.Contains(_childA, ids);
        Assert.DoesNotContain(_childB, ids);
    }
}
