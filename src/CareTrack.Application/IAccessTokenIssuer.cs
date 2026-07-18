namespace CareTrack.Application;

/// <summary>Issues signed access tokens. Implemented in Infrastructure (JWT).</summary>
public interface IAccessTokenIssuer
{
    /// <summary>Creates an access token for the user and returns it with its expiry.</summary>
    (string Token, DateTimeOffset ExpiresAt) Issue(Guid userId, string email, string displayName);
}
