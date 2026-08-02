namespace VpnDashboard.Data.Entities;

/// <summary>Тип источника subscription-ссылки.</summary>
public enum SubscriptionSourceKind
{
    /// <summary>Привязка к серверу Hiddify.</summary>
    Server = 0,

    /// <summary>Ручная ссылка конкретного пользователя.</summary>
    Manual = 1,

    /// <summary>Общая ссылка, назначаемая нескольким пользователям.</summary>
    Shared = 2
}

/// <summary>
/// Последний успешно полученный ответ провайдера по одному источнику.
/// Нужен, чтобы кратковременная недоступность провайдера не выкидывала его серверы
/// из подписки пользователя, и чтобы не ходить наружу на каждое обновление.
/// Ключуется источником, а не пользователем: общая подписка — одна ссылка на всех.
/// </summary>
public class SubscriptionSnapshot
{
    public Guid Id { get; set; }

    public SubscriptionSourceKind SourceKind { get; set; }

    /// <summary>Id привязки / ручной подписки / общей подписки.</summary>
    public Guid SourceId { get; set; }

    /// <summary>URL, с которого получен снимок. Смена URL инвалидирует кэш.</summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Нормализованный JSON-массив Xray-конфигов, сжатый gzip.
    /// Ответы провайдеров — сотни килобайт, поэтому храним сжатыми.
    /// Null — успешных ответов ещё не было.
    /// </summary>
    public byte[]? ContentGzip { get; set; }

    /// <summary>Число записей (серверов) в снимке.</summary>
    public int ConfigCount { get; set; }

    // Разобранный subscription-userinfo последнего успешного ответа.
    public long UploadBytes { get; set; }
    public long DownloadBytes { get; set; }

    /// <summary>Лимит трафика; 0 — безлимит (так же трактует его и клиент).</summary>
    public long TotalBytes { get; set; }

    /// <summary>Когда последний раз успешно получили содержимое. Null — ни разу.</summary>
    public DateTime? FetchedAt { get; set; }

    // Диагностика последней попытки (в том числе неудачной).
    public DateTime? LastAttemptAt { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }

    /// <summary>Формат последнего ответа для отображения в диагностике.</summary>
    public string? LastFormat { get; set; }
}
