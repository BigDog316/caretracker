using CareTrack.Application;
using CareTrack.Domain;
using Microsoft.EntityFrameworkCore;

namespace CareTrack.Infrastructure;

/// <summary>
/// EF Core / PostgreSQL implementation of the grant store. Every query filters
/// out revoked grants, so revoked access is never honored.
/// </summary>
public sealed class EfAccessGrantStore : IAccessGrantStore
{
    private readonly CareTrackDbContext _db;

    public EfAccessGrantStore(CareTrackDbContext db) => _db = db;

    public Task<AccessGrant?> FindActiveGrantAsync(
        Guid userId, Guid careProfileId, CancellationToken ct = default)
        => _db.AccessGrants
            .AsNoTracking()
            .SingleOrDefaultAsync(
                g => g.UserId == userId
                     && g.CareProfileId == careProfileId
                     && g.RevokedAt == null,
                ct);

    public async Task<IReadOnlyList<Guid>> ListAccessibleProfileIdsAsync(
        Guid userId, CancellationToken ct = default)
        => await _db.AccessGrants
            .AsNoTracking()
            .Where(g => g.UserId == userId && g.RevokedAt == null)
            .Select(g => g.CareProfileId)
            .Distinct()
            .ToListAsync(ct);
}
