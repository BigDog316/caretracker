namespace CareTrack.Client.Shared.Api;

/// <summary>DTOs mirroring the CareTrack API's JSON shapes.</summary>
public sealed record AuthResult(
    Guid UserId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt);

public sealed record CareProfileSummary(
    Guid Id, string DisplayName, DateOnly? DateOfBirth);

public sealed record FollowUpReminder(
    Guid AppointmentId, Guid CareProfileId, string Title,
    DateTimeOffset EndsAt, Guid? ProviderId);

/// <summary>Tokens persisted between sessions by the host platform.</summary>
public sealed record StoredTokens(
    Guid UserId, string AccessToken, string RefreshToken);

/// <summary>
/// Where tokens live is a per-host decision: browser localStorage on the web,
/// SecureStorage in the MAUI app. The shared pages only see this interface.
/// </summary>
public interface ITokenStore
{
    Task<StoredTokens?> LoadAsync();
    Task SaveAsync(StoredTokens tokens);
    Task ClearAsync();
}

/// <summary>Raised for expected API failures so pages can show a message.</summary>
public sealed class ApiException : Exception
{
    public ApiException(string message) : base(message) { }
}
