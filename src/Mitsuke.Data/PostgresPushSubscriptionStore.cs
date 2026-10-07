using Dapper;
using Mitsuke.Core;
using Npgsql;

namespace Mitsuke.Data;

public sealed class PostgresPushSubscriptionStore(NpgsqlDataSource db) : IPushSubscriptionStore
{
    public async Task<IReadOnlyList<PushSubscription>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        var rows = await conn.QueryAsync<(Guid Id, Guid UserId, string Endpoint, string P256dh, string Auth)>(
            "select id, user_id, endpoint, p256dh, auth from push_subscriptions where user_id = @userId order by created_at",
            new { userId });
        return rows.Select(r => new PushSubscription(r.Id, r.UserId, r.Endpoint, r.P256dh, r.Auth)).ToList();
    }

    public async Task SaveAsync(Guid userId, string endpoint, string p256dh, string auth, string? userAgent, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync("""
            insert into push_subscriptions (user_id, endpoint, p256dh, auth, user_agent)
            values (@userId, @endpoint, @p256dh, @auth, @userAgent)
            on conflict (endpoint) do update set
                user_id = excluded.user_id, p256dh = excluded.p256dh, auth = excluded.auth, user_agent = excluded.user_agent
            """, new { userId, endpoint, p256dh, auth, userAgent });
    }

    public async Task DeleteAsync(Guid userId, string endpoint, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync("delete from push_subscriptions where user_id = @userId and endpoint = @endpoint", new { userId, endpoint });
    }

    public async Task MarkDeliveredAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync("update push_subscriptions set last_ok_at = now() where id = @subscriptionId", new { subscriptionId });
    }

    public async Task RemoveAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
    {
        await using var conn = await db.OpenConnectionAsync(cancellationToken);
        await conn.ExecuteAsync("delete from push_subscriptions where id = @subscriptionId", new { subscriptionId });
    }
}
