using System.Security.Claims;
using CareTrack.Api.Auth;
using CareTrack.Application;
using CareTrack.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace CareTrack.Tests;

/// <summary>
/// Verifies the ASP.NET authorization handler correctly bridges to the access
/// service: it resolves the profile id from the route, honors the required
/// role, and fails closed on missing auth or a missing/invalid route id.
/// </summary>
public class CareProfileAccessHandlerTests
{
    private sealed class FixedCurrentUser : ICurrentUser
    {
        private readonly Guid? _id;
        public FixedCurrentUser(Guid? id) => _id = id;
        public Guid? UserId => _id;
        public Guid RequireUserId() => _id ?? throw new UnauthorizedAccessException();
    }

    private static IHttpContextAccessor HttpWithRoute(Guid? profileId)
    {
        var ctx = new DefaultHttpContext();
        if (profileId is not null)
            ctx.Request.RouteValues[CareProfileAccessHandler.RouteKey] =
                profileId.Value.ToString();
        return new HttpContextAccessor { HttpContext = ctx };
    }

    private static async Task<bool> Evaluate(
        Guid? userId, Guid? routeProfileId,
        InMemoryAccessGrantStore store, AccessRole required)
    {
        var access = new CareProfileAccessService(store);
        var handler = new CareProfileAccessHandler(
            new FixedCurrentUser(userId), access, HttpWithRoute(routeProfileId));

        var requirement = new CareProfileAccessRequirement(required);
        var ctx = new AuthorizationHandlerContext(
            new[] { requirement }, new ClaimsPrincipal(), resource: null);

        await handler.HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    [Fact]
    public async Task Grants_access_when_user_has_required_role()
    {
        var user = Guid.NewGuid();
        var profile = Guid.NewGuid();
        var store = new InMemoryAccessGrantStore();
        store.Add(user, profile, AccessRole.Editor);

        Assert.True(await Evaluate(user, profile, store, AccessRole.Viewer));
        Assert.True(await Evaluate(user, profile, store, AccessRole.Editor));
    }

    [Fact]
    public async Task Denies_when_role_insufficient()
    {
        var user = Guid.NewGuid();
        var profile = Guid.NewGuid();
        var store = new InMemoryAccessGrantStore();
        store.Add(user, profile, AccessRole.Viewer);

        Assert.False(await Evaluate(user, profile, store, AccessRole.Editor));
        Assert.False(await Evaluate(user, profile, store, AccessRole.Owner));
    }

    [Fact]
    public async Task Denies_when_no_grant()
    {
        var store = new InMemoryAccessGrantStore();
        Assert.False(await Evaluate(
            Guid.NewGuid(), Guid.NewGuid(), store, AccessRole.Viewer));
    }

    [Fact]
    public async Task Denies_when_unauthenticated()
    {
        var profile = Guid.NewGuid();
        var store = new InMemoryAccessGrantStore();
        store.Add(Guid.NewGuid(), profile, AccessRole.Owner);

        Assert.False(await Evaluate(null, profile, store, AccessRole.Viewer));
    }

    [Fact]
    public async Task Denies_when_route_has_no_profile_id()
    {
        var user = Guid.NewGuid();
        var store = new InMemoryAccessGrantStore();
        store.Add(user, Guid.NewGuid(), AccessRole.Owner);

        Assert.False(await Evaluate(user, null, store, AccessRole.Viewer));
    }
}
