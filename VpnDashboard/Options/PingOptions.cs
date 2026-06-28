namespace VpnDashboard.Options;

/// <summary>
/// Настройки фонового пинг-сервиса, опрашивающего панели Hiddify.
/// </summary>
public sealed class PingOptions
{
    public const string SectionName = "Ping";

    /// <summary>Интервал между циклами пинга, минуты.</summary>
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Таймаут одного HTTP-запроса к панели, секунды.</summary>
    public int TimeoutSeconds { get; set; } = 8;

    /// <summary>Включён ли фоновый пинг.</summary>
    public bool Enabled { get; set; } = true;
}
