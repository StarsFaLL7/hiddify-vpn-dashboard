namespace VpnDashboard.Options;

/// <summary>
/// Настройки проверки работоспособности серверов, которые провайдеры отдают в подписках.
/// <para>
/// Проверка идёт с нашего хоста, поэтому отвечает на вопрос «сервер жив и учётные данные
/// приняты», а не «работает у конкретного пользователя»: чужой провайдер или ISP
/// пользователя могут резать то, что у нас открывается.
/// </para>
/// </summary>
public sealed class ProbeOptions
{
    public const string SectionName = "Probe";

    /// <summary>Включена ли фоновая проверка. При false здоровье не обновляется и фильтр не работает.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Интервал между полными обходами, минуты.</summary>
    public int IntervalMinutes { get; set; } = 180;

    /// <summary>
    /// Задержка первого обхода после старта, секунды. Даёт приложению подняться
    /// и не превращает рестарт в мгновенный шквал подключений к провайдерам.
    /// </summary>
    public int StartupDelaySeconds { get; set; } = 60;

    /// <summary>
    /// Сколько серверов проверяем одновременно. Провайдеры считают одновременные подключения,
    /// поэтому запас в большую сторону здесь вредит.
    /// </summary>
    public int Concurrency { get; set; } = 4;

    /// <summary>Таймаут TCP-подключения и TLS-хендшейка, секунды.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Проверять ли соединение по-настоящему, поднимая ядро Xray. При false остаётся
    /// только проверка доступности порта.
    /// </summary>
    public bool XrayEnabled { get; set; } = true;

    /// <summary>Путь к бинарю xray. По умолчанию ищется в PATH; в образе лежит в /usr/local/bin.</summary>
    public string XrayPath { get; set; } = "xray";

    /// <summary>Сколько ждём ответа через поднятый прокси, секунды.</summary>
    public int XrayTimeoutSeconds { get; set; } = 12;

    /// <summary>
    /// Адрес, который дёргаем через прокси. Нужен маленький и стабильный ответ;
    /// generate_204 отдаёт пустой 204.
    /// </summary>
    public string ProbeUrl { get; set; } = "http://cp.cloudflare.com/generate_204";

    /// <summary>Выбрасывать ли нерабочие серверы из подписки. При false проверка только показывается в админке.</summary>
    public bool FilterEnabled { get; set; } = true;

    /// <summary>
    /// Сколько проверок подряд должно провалиться, чтобы сервер перестал попадать в подписку.
    /// Одна неудача — это чаще всего сеть моргнула, а не сервер умер.
    /// </summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>
    /// Предохранитель: если фильтр срезает больше этой доли записей, он не применяется вовсе.
    /// Массовое падение почти всегда означает нашу проблему, а не смерть всех провайдеров сразу.
    /// </summary>
    public double MaxDropFraction { get; set; } = 0.5;

    /// <summary>Через сколько дней забывать сервер, который перестал встречаться в подписках.</summary>
    public int ForgetAfterDays { get; set; } = 14;
}
