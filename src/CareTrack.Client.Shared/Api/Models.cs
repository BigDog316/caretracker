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

public sealed record ProviderSummary(
    Guid Id, string Name, string? Organization, string? Specialty,
    string? Address, string? Phone);

public sealed record AppointmentSummary(
    Guid Id, string Title, string? Location,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    Guid? ProviderId, DateTimeOffset? FollowUpCompletedAt);

public sealed record NoteSummary(
    Guid Id, string Body, Guid? AppointmentId, Guid? ProviderId,
    DateTimeOffset CreatedAt);

public sealed record DocumentTagDto(string Value);

public sealed record DocumentSummary(
    Guid Id, string FileName, string ContentType, long SizeBytes,
    string? Description, DateTimeOffset CreatedAt,
    IReadOnlyList<DocumentTagDto> Tags);

public sealed record FileDownload(
    string FileName, string ContentType, byte[] Content);

public sealed record SchoolPlanSummary(
    Guid Id, string Type, string? School, string? Title,
    DateOnly? EffectiveOn, DateOnly? ReviewDueOn, Guid? DocumentId,
    string? Notes);

public sealed record AgencySummary(
    Guid Id, string Name, string Kind, string? ContactName,
    string? Phone, string? Email, string? Address, string? Notes);

/// <summary>
/// Saves a downloaded file on the host platform (browser blob download on
/// the web; file system + share sheet in the MAUI app).
/// </summary>
public interface IFileSaver
{
    Task SaveAsync(FileDownload file);
}

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
