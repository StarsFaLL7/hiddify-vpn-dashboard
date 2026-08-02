namespace VpnDashboard.Data.Entities;

/// <summary>
/// Локальный пользователь сервиса. Создаётся независимо от привязок к серверам Hiddify.
/// </summary>
public class LocalUser
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Криптослучайный токен в URL витрины (base64url). Уникален.</summary>
    public string ShowcaseToken { get; set; } = string.Empty;

    public string? Comment { get; set; }

    /// <summary>
    /// Когда VPN-клиент этого пользователя последний раз забирал агрегированную подписку.
    /// Null — ни разу; значит ссылку либо не добавили в клиент, либо только открывали в браузере.
    /// </summary>
    public DateTime? LastSubscriptionFetchAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<UserServerBinding> Bindings { get; set; } = [];

    public List<ManualSubscription> ManualSubscriptions { get; set; } = [];

    public List<UserGlobalSubscription> GlobalSubscriptions { get; set; } = [];
}
