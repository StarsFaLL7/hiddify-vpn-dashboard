namespace VpnDashboard.Data.Entities;

/// <summary>
/// Привязка локального пользователя к Hiddify-юзеру на конкретном сервере.
/// Один локальный пользователь может иметь РАЗНЫЕ UUID на разных серверах.
/// </summary>
public class UserServerBinding
{
    public Guid Id { get; set; }

    public Guid LocalUserId { get; set; }

    public LocalUser LocalUser { get; set; } = null!;

    public Guid HiddifyServerId { get; set; }

    public HiddifyServer HiddifyServer { get; set; } = null!;

    /// <summary>UUID этого пользователя на данном сервере.</summary>
    public string HiddifyUuid { get; set; } = string.Empty;

    /// <summary>Показывать ли эту подписку на витрине.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>UUID не найден на сервере при последней синхронизации.</summary>
    public bool IsBroken { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
