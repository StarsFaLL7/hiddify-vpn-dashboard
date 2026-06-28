namespace VpnDashboard.Data.Entities;

/// <summary>
/// Справочник серверов Hiddify. Хранит метаданные и секреты доступа к admin-API.
/// Приватные ключи VPN-конфигов здесь НЕ хранятся.
/// </summary>
public class HiddifyServer
{
    public Guid Id { get; set; }

    /// <summary>Человекочитаемое имя, например «NL-01».</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Домен сервера (без схемы и слешей), например «vpn.example.com».</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Proxy Path for Admins — используется для вызовов admin-API.</summary>
    public string AdminProxyPath { get; set; } = string.Empty;

    /// <summary>Proxy Path for Clients — используется для сборки subscription-ссылок витрины.</summary>
    public string ClientProxyPath { get; set; } = string.Empty;

    /// <summary>Hiddify-API-Key (admin UUID) для заголовка авторизации.</summary>
    public string AdminUuid { get; set; } = string.Empty;

    /// <summary>Результат последнего пинга: null — ещё не проверяли.</summary>
    public bool? IsOnline { get; set; }

    public DateTime? LastPingAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<UserServerBinding> Bindings { get; set; } = [];
}
