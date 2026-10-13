namespace VpnDashboard.Data.Entities;

/// <summary>
/// Общая subscription-ссылка, которую можно назначить нескольким пользователям.
/// </summary>
public class GlobalSubscription
{
    public Guid Id { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    /// <summary>Глобальный выключатель: при false ссылка не показывается никому.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Короткий префикс имён серверов в агрегированной подписке. Пусто — берётся <see cref="Label"/>.</summary>
    public string? Prefix { get; set; }

    /// <summary>User-Agent для запроса подписки. Пусто — дефолт из конфига.</summary>
    public string? UpstreamUserAgent { get; set; }

    /// <summary>
    /// Место в списке общих подписок: в этом порядке они показываются в админке
    /// и попадают в выдачу пользователям.
    /// </summary>
    public int Position { get; set; }

    /// <summary>Назначать подписку каждому новому пользователю при его создании.</summary>
    public bool AssignToNewUsers { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<UserGlobalSubscription> Assignments { get; set; } = [];
}
