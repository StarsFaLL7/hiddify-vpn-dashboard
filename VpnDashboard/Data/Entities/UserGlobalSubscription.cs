namespace VpnDashboard.Data.Entities;

/// <summary>
/// Связь many-to-many: каким пользователям назначена общая подписка.
/// </summary>
public class UserGlobalSubscription
{
    public Guid LocalUserId { get; set; }

    public LocalUser LocalUser { get; set; } = null!;

    public Guid GlobalSubscriptionId { get; set; }

    public GlobalSubscription GlobalSubscription { get; set; } = null!;
}
