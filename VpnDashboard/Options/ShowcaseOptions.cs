namespace VpnDashboard.Options;

/// <summary>
/// Настройки публичной витрины подписок.
/// </summary>
public sealed class ShowcaseOptions
{
    public const string SectionName = "Showcase";

    /// <summary>Короткая инструкция «как добавить ссылку в клиент», показывается на витрине.</summary>
    public string Instructions { get; set; } =
        "Скопируйте ссылку и добавьте её как подписку в приложении INCY, Happ, v2rayN или совместимом клиенте. " +
        "Либо отсканируйте QR-код камерой клиента.";
}
