using CareTrack.Domain;
using Microsoft.AspNetCore.Authorization;

namespace CareTrack.Api.Auth;

/// <summary>
/// Authorization requirement asserting the caller holds at least
/// <see cref="RequiredRole"/> on the care profile identified in the route.
/// </summary>
public sealed class CareProfileAccessRequirement : IAuthorizationRequirement
{
    public CareProfileAccessRequirement(AccessRole requiredRole)
        => RequiredRole = requiredRole;

    public AccessRole RequiredRole { get; }
}
