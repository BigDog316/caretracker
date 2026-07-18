using System.Security.Cryptography;
using CareTrack.Application;
using CareTrack.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CareTrack.Infrastructure.Identity;

/// <summary>
/// Registration, sign-in, and refresh-token rotation on top of ASP.NET Core
/// Identity. On register it creates the Identity <see cref="AppUser"/> and the
/// domain <see cref="User"/> sharing one id, so the JWT subject is directly the
/// domain user id used in access checks.
/// </summary>
public sealed class IdentityAuthService : IAuthService
{
    private readonly UserManager<AppUser> _users;
    private readonly CareTrackDbContext _db;
    private readonly IAccessTokenIssuer _tokens;
    private readonly JwtOptions _jwt;
    private readonly IClock _clock;

    public IdentityAuthService(
        UserManager<AppUser> users,
        CareTrackDbContext db,
        IAccessTokenIssuer tokens,
        IOptions<JwtOptions> jwt,
        IClock clock)
    {
        _users = users;
        _db = db;
        _tokens = tokens;
        _jwt = jwt.Value;
        _clock = clock;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest req, CancellationToken ct = default)
    {
        var existing = await _users.FindByEmailAsync(req.Email);
        if (existing is not null)
            throw new AuthException("Registration failed."); // vague on purpose

        var id = Guid.NewGuid();
        var appUser = new AppUser
        {
            Id = id,
            UserName = req.Email,
            Email = req.Email,
            DisplayName = req.DisplayName
        };

        // Create the domain user under the same id, atomically with the identity
        // user via a transaction.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var created = await _users.CreateAsync(appUser, req.Password);
        if (!created.Succeeded)
            throw new AuthException(
                "Registration failed: " +
                string.Join("; ", created.Errors.Select(e => e.Description)));

        _db.Users.Add(new User
        {
            Id = id,
            Email = req.Email,
            DisplayName = req.DisplayName
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await IssuePairAsync(appUser, ct);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest req, CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(req.Email);
        if (user is null || !await _users.CheckPasswordAsync(user, req.Password))
            throw new AuthException();

        return await IssuePairAsync(user, ct);
    }

    public async Task<AuthResult> RefreshAsync(RefreshRequest req, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var stored = await _db.RefreshTokens
            .SingleOrDefaultAsync(t => t.Token == req.RefreshToken, ct);

        if (stored is null || !stored.IsActive(now))
            throw new AuthException("Invalid refresh token.");

        var user = await _users.FindByIdAsync(stored.UserId.ToString());
        if (user is null)
            throw new AuthException("Invalid refresh token.");

        // Rotate: revoke the old token, issue a fresh pair.
        stored.RevokedAt = now;
        var result = await IssuePairAsync(user, ct, replacingTokenId: stored.Id);
        await _db.SaveChangesAsync(ct);
        return result;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var stored = await _db.RefreshTokens
            .SingleOrDefaultAsync(t => t.Token == refreshToken, ct);
        if (stored is not null && stored.RevokedAt is null)
        {
            stored.RevokedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<AuthResult> IssuePairAsync(
        AppUser user, CancellationToken ct, Guid? replacingTokenId = null)
    {
        var (access, accessExpires) =
            _tokens.Issue(user.Id, user.Email!, user.DisplayName);

        var refresh = new RefreshToken
        {
            UserId = user.Id,
            Token = GenerateRefreshToken(),
            ExpiresAt = _clock.UtcNow.AddDays(_jwt.RefreshTokenDays)
        };
        _db.RefreshTokens.Add(refresh);

        if (replacingTokenId is Guid oldId)
        {
            var old = await _db.RefreshTokens.FindAsync(new object?[] { oldId }, ct);
            if (old is not null) old.ReplacedByTokenId = refresh.Id;
        }

        await _db.SaveChangesAsync(ct);

        return new AuthResult(user.Id, access, refresh.Token, accessExpires);
    }

    private static string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}
