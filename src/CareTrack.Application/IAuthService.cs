namespace CareTrack.Application;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);

public sealed record AuthResult(
    Guid UserId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt);

/// <summary>
/// Registration, sign-in, and refresh-token rotation. The concrete
/// implementation lives in Infrastructure (it depends on ASP.NET Identity), but
/// the surface is exposed here so controllers depend only on the app layer.
/// Throws <see cref="AuthException"/> on any failure, deliberately vague so as
/// not to reveal whether an email exists.
/// </summary>
public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest req, CancellationToken ct = default);
    Task<AuthResult> LoginAsync(LoginRequest req, CancellationToken ct = default);
    Task<AuthResult> RefreshAsync(RefreshRequest req, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
}

public sealed class AuthException : Exception
{
    public AuthException(string message = "Invalid credentials.") : base(message) { }
}
