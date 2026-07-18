namespace CareTrack.Application;

/// <summary>
/// Supplies the authenticated caller's identity to the application layer without
/// coupling it to ASP.NET's HttpContext. The API layer provides the concrete
/// implementation that reads the JWT; tests can substitute a fixed value.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The authenticated user's id, or null if unauthenticated.</summary>
    Guid? UserId { get; }

    /// <summary>The user id, throwing if the caller is not authenticated.</summary>
    Guid RequireUserId();
}
