using Microsoft.EntityFrameworkCore;

namespace CareTrack.Infrastructure.Calendar;

/// <summary>
/// A user's Google Calendar link. Holds the OAuth refresh token obtained when
/// they connected their calendar. Dev-grade like <c>RefreshToken.Token</c>:
/// stored raw — encrypt at rest (Data Protection / KMS) before shipping,
/// alongside the other items on the hardening list.
/// </summary>
public class GoogleCalendarConnection
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid UserId { get; set; }
    public required string RefreshToken { get; set; }
    public DateTimeOffset ConnectedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Storage for calendar connections; interface so sync is testable.</summary>
public interface IGoogleCalendarConnectionStore
{
    Task<GoogleCalendarConnection?> GetAsync(Guid userId, CancellationToken ct = default);
    Task UpsertAsync(Guid userId, string refreshToken, CancellationToken ct = default);
    Task RemoveAsync(Guid userId, CancellationToken ct = default);
}

public sealed class EfGoogleCalendarConnectionStore : IGoogleCalendarConnectionStore
{
    private readonly CareTrackDbContext _db;

    public EfGoogleCalendarConnectionStore(CareTrackDbContext db) => _db = db;

    public Task<GoogleCalendarConnection?> GetAsync(
        Guid userId, CancellationToken ct = default)
        => _db.GoogleCalendarConnections.AsNoTracking()
            .SingleOrDefaultAsync(c => c.UserId == userId, ct);

    public async Task UpsertAsync(
        Guid userId, string refreshToken, CancellationToken ct = default)
    {
        var existing = await _db.GoogleCalendarConnections
            .SingleOrDefaultAsync(c => c.UserId == userId, ct);
        if (existing is null)
            _db.GoogleCalendarConnections.Add(new GoogleCalendarConnection
            {
                UserId = userId,
                RefreshToken = refreshToken
            });
        else
            existing.RefreshToken = refreshToken;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid userId, CancellationToken ct = default)
    {
        var existing = await _db.GoogleCalendarConnections
            .SingleOrDefaultAsync(c => c.UserId == userId, ct);
        if (existing is not null)
        {
            _db.GoogleCalendarConnections.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
    }
}
