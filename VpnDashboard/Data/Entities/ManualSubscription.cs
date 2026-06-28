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

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
