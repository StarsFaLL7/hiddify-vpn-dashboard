using System.Collections.Concurrent;
using VpnDashboard.Data.Entities;

namespace VpnDashboard.Subscriptions;

/// <summary>
/// Замки на источник: не даёт нескольким одновременным обновлениям подписки
/// (у пользователя несколько устройств, а общая подписка ещё и у нескольких пользователей)
/// превратиться в пачку одинаковых запросов к провайдеру.
/// </summary>
public sealed class SourceLockRegistry
{
    private readonly ConcurrentDictionary<(SubscriptionSourceKind, Guid), SemaphoreSlim> _locks = new();

    public SemaphoreSlim For(SubscriptionSourceKind kind, Guid sourceId) =>
        _locks.GetOrAdd((kind, sourceId), _ => new SemaphoreSlim(1, 1));
}
