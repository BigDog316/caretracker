using Microsoft.AspNetCore.Authorization;

namespace CareTrack.Api.Auth;

/// <summary>Well-known policy names for care-profile access at each role.</summary>
public static class AccessPolicies
{
    public const string Viewer = "CareProfile.Viewer";
    public const string Editor = "CareProfile.Editor";
    public const string Owner  = "CareProfile.Owner";
}

/// <summary>
/// Convenience: [RequireCareProfile(AccessRole.Editor)] on an action reads
/// naturally. Maps to the matching policy. The route must contain a
/// "{careProfileId}" segment for the handler to resolve.
/// </summary>
public sealed class RequireCareProfileAttribute : AuthorizeAttribute
{
    public RequireCareProfileAttribute(Domain.AccessRole role)
        => Policy = role switch
        {
            Domain.AccessRole.Owner  => AccessPolicies.Owner,
            Domain.AccessRole.Editor => AccessPolicies.Editor,
            _                        => AccessPolicies.Viewer
        };
}
