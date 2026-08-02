namespace VpnDashboard.Data.Entities;

/// <summary>
/// Ручная subscription-ссылка, заданная админом для конкретного пользователя.
/// </summary>
public class ManualSubscription
{
    public Guid Id { get; set; }

    public Guid LocalUserId { get; set; }

    public LocalUser LocalUser { get; set; } = null!;

    public string Label { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Короткий префикс имён серверов в агрегированной подписке. Пусто — берётся <see cref="Label"/>.</summary>
    public string? Prefix { get; set; }

    /// <summary>User-Agent для запроса подписки. Пусто — дефолт из конфига.</summary>
    public string? UpstreamUserAgent { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
