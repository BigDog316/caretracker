using System.Security.Claims;
using CareTrack.Application;

namespace CareTrack.Api.Auth;

/// <summary>
/// Reads the caller's user id from the JWT's subject/NameIdentifier claim.
/// </summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor)
        => _accessor = accessor;

    public Guid? UserId
    {
        get
        {
            var principal = _accessor.HttpContext?.User;
            var raw = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal?.FindFirstValue("sub");
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public Guid RequireUserId()
        => UserId ?? throw new UnauthorizedAccessException("No authenticated user.");
}
