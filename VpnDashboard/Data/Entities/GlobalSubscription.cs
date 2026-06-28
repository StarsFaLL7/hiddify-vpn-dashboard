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

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<UserGlobalSubscription> Assignments { get; set; } = [];
}
