namespace VpnDashboard.Data.Entities;

/// <summary>
/// Результат проверки одного сервера из подписки провайдера.
/// <para>
/// Ключуется конфигом без учётных данных, а не записью пользователя: привязки Hiddify
/// персональные, и один и тот же сервер приходит десяткам людей с разными UUID.
/// Проверять каждую копию отдельно — это и лишние подключения, и риск упереться
/// в лимиты панели. Обратная сторона: индивидуально протухший UUID так не виден.
/// </para>
/// </summary>
public class ServerHealth
{
    public Guid Id { get; set; }

    /// <summary>Хэш аутбаунда без учётных данных и имени — общий для всех пользователей этого сервера.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Имя записи в момент последней проверки — только для админки.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public string Protocol { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }

    /// <summary>tls / reality / none.</summary>
    public string? Security { get; set; }

    /// <summary>Транспорт: tcp, grpc, ws, xhttp…</summary>
    public string? Network { get; set; }

    /// <summary>
    /// Умеем ли мы проверять такой сервер. Непроверяемые (hysteria2 и прочий UDP)
    /// никогда не выбрасываются из подписки — отсутствие проверки не повод их терять.
    /// </summary>
    public bool IsSupported { get; set; } = true;

    /// <summary>Итог последней проверки.</summary>
    public bool IsOk { get; set; }

    /// <summary>Сколько проверок подряд провалилось. Успех обнуляет.</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>Время ответа последней удачной проверки, мс.</summary>
    public int? LatencyMs { get; set; }

    /// <summary>Чем проверяли: xray, tls, tcp.</summary>
    public string? Method { get; set; }

    public string? LastError { get; set; }
    public DateTime? LastCheckAt { get; set; }
    public DateTime? LastOkAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
