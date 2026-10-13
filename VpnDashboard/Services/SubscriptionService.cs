using Microsoft.EntityFrameworkCore;
using VpnDashboard.Data;
using VpnDashboard.Data.Entities;

namespace VpnDashboard.Services;

/// <summary>Итог последнего опроса общей подписки — для списка в админке.</summary>
/// <param name="ConfigCount">Число серверов в сохранённом снимке.</param>
/// <param name="FetchedAt">Когда снимок последний раз удалось обновить; null — ни разу.</param>
/// <param name="LastError">Ошибка либо замечание последней попытки.</param>
/// <param name="LastAttemptFailed">Последняя попытка не удалась; отдаётся прежний снимок, если он есть.</param>
public sealed record GlobalSourceState(int ConfigCount, DateTime? FetchedAt, string? LastError, bool LastAttemptFailed);

/// <summary>Ручные подписки пользователя и общие подписки с назначениями (many-to-many).</summary>
public sealed class SubscriptionService(IDbContextFactory<AppDbContext> dbFactory)
{
    // ---- Ручные подписки ----

    public async Task<ManualSubscription> AddManualAsync(
        Guid userId, string label, string url, string? prefix = null, string? userAgent = null,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sub = new ManualSubscription
        {
            Id = Guid.NewGuid(),
            LocalUserId = userId,
            Label = label.Trim(),
            Url = url.Trim(),
            Prefix = prefix,
            UpstreamUserAgent = userAgent,
            IsEnabled = true
        };
        db.ManualSubscriptions.Add(sub);
        await db.SaveChangesAsync(ct);
        return sub;
    }

    public async Task UpdateManualAsync(
        Guid id, string label, string url, bool isEnabled, string? prefix = null, string? userAgent = null,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sub = await db.ManualSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sub is null)
            return;

        sub.Label = label.Trim();
        sub.Url = url.Trim();
        sub.IsEnabled = isEnabled;
        sub.Prefix = prefix;
        sub.UpstreamUserAgent = userAgent;
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
            .OrderBy(g => g.Position)
            .ThenBy(g => g.Label)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    /// <summary>
    /// Состояние общих подписок по их снимкам. Подписки, за которыми ещё ни разу не ходили,
    /// в словарь не попадают.
    /// </summary>
    public async Task<Dictionary<Guid, GlobalSourceState>> GetGlobalStatesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Само содержимое снимка не тянем: это сотни килобайт на источник.
        var rows = await db.SubscriptionSnapshots
            .Where(s => s.SourceKind == SubscriptionSourceKind.Shared)
            .Select(s => new { s.SourceId, s.ConfigCount, s.FetchedAt, s.LastAttemptAt, s.LastError })
            .ToListAsync(ct);

        return rows.ToDictionary(
            r => r.SourceId,
            r => new GlobalSourceState(
                r.FetchedAt is null ? 0 : r.ConfigCount,
                r.FetchedAt,
                r.LastError,
                r.FetchedAt is null || r.LastAttemptAt > r.FetchedAt));
    }

    public async Task<GlobalSubscription> CreateGlobalAsync(
        string label, string url, string? prefix = null, string? userAgent = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Новая подписка встаёт в конец списка.
        var last = await db.GlobalSubscriptions.MaxAsync(g => (int?)g.Position, ct);
        var sub = new GlobalSubscription
        {
            Id = Guid.NewGuid(),
            Label = label.Trim(),
            Url = url.Trim(),
            Prefix = prefix,
            UpstreamUserAgent = userAgent,
            IsEnabled = true,
            Position = (last ?? -1) + 1
        };
        db.GlobalSubscriptions.Add(sub);
        await db.SaveChangesAsync(ct);
        return sub;
    }

    public async Task UpdateGlobalAsync(
        Guid id, string label, string url, bool isEnabled, string? prefix = null, string? userAgent = null,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sub = await db.GlobalSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sub is null)
            return;

        sub.Label = label.Trim();
        sub.Url = url.Trim();
        sub.IsEnabled = isEnabled;
        sub.Prefix = prefix;
        sub.UpstreamUserAgent = userAgent;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Сдвигает общую подписку в списке на <paramref name="delta"/> позиций (минус — вверх).
    /// Позиции перенумеровываются целиком, поэтому дыры и совпадения после удалений
    /// исправляются сами.
    /// </summary>
    public async Task MoveGlobalAsync(Guid id, int delta, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var subs = await db.GlobalSubscriptions
            .OrderBy(g => g.Position)
            .ThenBy(g => g.Label)
            .ToListAsync(ct);

        var from = subs.FindIndex(s => s.Id == id);
        if (from < 0)
            return;

        var to = Math.Clamp(from + delta, 0, subs.Count - 1);
        var moved = subs[from];
        subs.RemoveAt(from);
        subs.Insert(to, moved);

        for (var i = 0; i < subs.Count; i++)
            subs[i].Position = i;

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

    /// <summary>Назначает общую подписку всем пользователям либо снимает её со всех.</summary>
    public async Task SetAssignmentForAllAsync(Guid globalSubscriptionId, bool assigned, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (!assigned)
        {
            await db.UserGlobalSubscriptions
                .Where(x => x.GlobalSubscriptionId == globalSubscriptionId)
                .ExecuteDeleteAsync(ct);
            return;
        }

        var missing = await db.LocalUsers
            .Where(u => u.GlobalSubscriptions.All(x => x.GlobalSubscriptionId != globalSubscriptionId))
            .Select(u => u.Id)
            .ToListAsync(ct);

        db.UserGlobalSubscriptions.AddRange(missing.Select(userId => new UserGlobalSubscription
        {
            LocalUserId = userId,
            GlobalSubscriptionId = globalSubscriptionId
        }));
        await db.SaveChangesAsync(ct);
    }

    public async Task SetAssignToNewUsersAsync(Guid globalSubscriptionId, bool value, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sub = await db.GlobalSubscriptions.FirstOrDefaultAsync(s => s.Id == globalSubscriptionId, ct);
        if (sub is null)
            return;

        sub.AssignToNewUsers = value;
        await db.SaveChangesAsync(ct);
    }
}
