using CareTrack.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace CareTrack.Api.Auth;

/// <summary>
/// Bridges ASP.NET authorization to the application's access service. It pulls
/// the care profile id from the route (the "careProfileId" route value) and
/// asks <see cref="CareProfileAccessService"/> whether the current user may act
/// at the required role. This keeps the single authorization rule in the
/// application layer while still letting controllers use [Authorize] policies.
/// </summary>
public sealed class CareProfileAccessHandler
    : AuthorizationHandler<CareProfileAccessRequirement>
{
    public const string RouteKey = "careProfileId";

    private readonly ICurrentUser _currentUser;
    private readonly CareProfileAccessService _access;
    private readonly IHttpContextAccessor _http;

    public CareProfileAccessHandler(
        ICurrentUser currentUser,
        CareProfileAccessService access,
        IHttpContextAccessor http)
    {
        _currentUser = currentUser;
        _access = access;
        _http = http;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CareProfileAccessRequirement requirement)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return; // unauthenticated -> requirement not met

        var routeValue = _http.HttpContext?.Request.RouteValues
            .TryGetValue(RouteKey, out var v) == true ? v?.ToString() : null;
        if (!Guid.TryParse(routeValue, out var careProfileId))
            return; // no/invalid profile id in route -> cannot satisfy

        if (await _access.HasAccessAsync(userId.Value, careProfileId, requirement.RequiredRole))
            context.Succeed(requirement);
    }
}
