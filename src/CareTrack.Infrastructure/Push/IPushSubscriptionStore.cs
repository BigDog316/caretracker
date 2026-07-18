using Microsoft.EntityFrameworkCore;

namespace CareTrack.Infrastructure.Push;

/// <summary>
/// Storage for Web Push subscriptions. Interface exists so the delivery path
/// can be unit-tested without a database.
/// </summary>
public interface IPushSubscriptionStore
{
    Task<IReadOnlyList<UserPushSubscription>> ListForUserAsync(
        Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Registers (or re-registers) a subscription. Keyed by endpoint: if the
    /// endpoint already exists its keys and owner are refreshed.
    /// </summary>
    Task UpsertAsync(UserPushSubscription subscription, CancellationToken ct = default);

    /// <summary>Removes the caller's subscription for the endpoint, if any.</summary>
    Task RemoveAsync(Guid userId, string endpoint, CancellationToken ct = default);

    /// <summary>Prunes a subscription the push service reported as gone.</summary>
    Task RemoveAsync(UserPushSubscription subscription, CancellationToken ct = default);
}

public sealed class EfPushSubscriptionStore : IPushSubscriptionStore
{
    private readonly CareTrackDbContext _db;

    public EfPushSubscriptionStore(CareTrackDbContext db) => _db = db;

    public async Task<IReadOnlyList<UserPushSubscription>> ListForUserAsync(
        Guid userId, CancellationToken ct = default)
        => await _db.PushSubscriptions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

    public async Task UpsertAsync(
        UserPushSubscription subscription, CancellationToken ct = default)
    {
        var existing = await _db.PushSubscriptions
            .SingleOrDefaultAsync(s => s.Endpoint == subscription.Endpoint, ct);
        if (existing is null)
        {
            _db.PushSubscriptions.Add(subscription);
        }
        else
        {
            existing.UserId = subscription.UserId;
            existing.P256dh = subscription.P256dh;
            existing.Auth = subscription.Auth;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(
        Guid userId, string endpoint, CancellationToken ct = default)
    {
        var existing = await _db.PushSubscriptions.SingleOrDefaultAsync(
            s => s.UserId == userId && s.Endpoint == endpoint, ct);
        if (existing is not null)
        {
            _db.PushSubscriptions.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task RemoveAsync(
        UserPushSubscription subscription, CancellationToken ct = default)
    {
        var tracked = await _db.PushSubscriptions.FindAsync(
            new object?[] { subscription.Id }, ct);
        if (tracked is not null)
        {
            _db.PushSubscriptions.Remove(tracked);
            await _db.SaveChangesAsync(ct);
        }
    }
}
