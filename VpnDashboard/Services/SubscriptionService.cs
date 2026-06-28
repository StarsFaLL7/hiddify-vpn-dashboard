using Microsoft.EntityFrameworkCore;
using VpnDashboard.Data;
using VpnDashboard.Data.Entities;

namespace VpnDashboard.Services;

/// <summary>Ручные подписки пользователя и общие подписки с назначениями (many-to-many).</summary>
public sealed class SubscriptionService(IDbContextFactory<AppDbContext> dbFactory)
{
    // ---- Ручные подписки ----

    public async Task<ManualSubscription> AddManualAsync(Guid userId, string label, string url, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sub = new ManualSubscription
        {
            Id = Guid.NewGuid(),
            LocalUserId = userId,
            Label = label.Trim(),
            Url = url.Trim(),
            IsEnabled = true
        };
        db.ManualSubscriptions.Add(sub);
        await db.SaveChangesAsync(ct);
        return sub;
    }

    public async Task UpdateManualAsync(Guid id, string label, string url, bool isEnabled, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sub = await db.ManualSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sub is null)
            return;

        sub.Label = label.Trim();
        sub.Url = url.Trim();
        sub.IsEnabled = isEnabled;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveManualAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sub = await db.ManualSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sub is null)
            return;

        db.ManualSubscriptions.Remove(sub);
        await db.SaveChangesAsync(ct);
    }

    // ---- Общие подписки ----

    public async Task<List<GlobalSubscription>> GetAllGlobalAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.GlobalSubscriptions
            .Include(g => g.Assignments)
            .OrderBy(g => g.Label)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<GlobalSubscription> CreateGlobalAsync(string label, string url, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sub = new GlobalSubscription
        {
            Id = Guid.NewGuid(),
            Label = label.Trim(),
            Url = url.Trim(),
            IsEnabled = true
        };
        db.GlobalSubscriptions.Add(sub);
        await db.SaveChangesAsync(ct);
        return sub;
    }

    public async Task UpdateGlobalAsync(Guid id, string label, string url, bool isEnabled, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sub = await db.GlobalSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sub is null)
            return;

        sub.Label = label.Trim();
        sub.Url = url.Trim();
        sub.IsEnabled = isEnabled;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteGlobalAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sub = await db.GlobalSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sub is null)
            return;

        db.GlobalSubscriptions.Remove(sub);
        await db.SaveChangesAsync(ct);
    }

    // ---- Назначения общих подписок пользователям ----

    /// <summary>Идентификаторы пользователей, которым назначена данная общая подписка.</summary>
    public async Task<HashSet<Guid>> GetAssignedUserIdsAsync(Guid globalSubscriptionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = await db.UserGlobalSubscriptions
            .Where(x => x.GlobalSubscriptionId == globalSubscriptionId)
            .Select(x => x.LocalUserId)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    public async Task<HashSet<Guid>> GetAssignedGlobalIdsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = await db.UserGlobalSubscriptions
            .Where(x => x.LocalUserId == userId)
            .Select(x => x.GlobalSubscriptionId)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    public async Task SetAssignmentAsync(Guid userId, Guid globalSubscriptionId, bool assigned, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.UserGlobalSubscriptions
            .FirstOrDefaultAsync(x => x.LocalUserId == userId && x.GlobalSubscriptionId == globalSubscriptionId, ct);

        if (assigned && existing is null)
        {
            db.UserGlobalSubscriptions.Add(new UserGlobalSubscription
            {
                LocalUserId = userId,
                GlobalSubscriptionId = globalSubscriptionId
            });
            await db.SaveChangesAsync(ct);
        }
        else if (!assigned && existing is not null)
        {
            db.UserGlobalSubscriptions.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
    }
}
