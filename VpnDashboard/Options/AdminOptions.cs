namespace VpnDashboard.Options;

/// <summary>
/// Настройки доступа к админ-панели: секретный префикс пути и учётные данные администратора.
/// Заполняется из конфигурации/переменных окружения, секреты не хранятся в репозитории.
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>Первый GUID-сегмент секретного пути админки.</summary>
    public string SecretPathSegment1 { get; set; } = string.Empty;

    /// <summary>Второй GUID-сегмент секретного пути админки.</summary>
    public string SecretPathSegment2 { get; set; } = string.Empty;

    /// <summary>PBKDF2-хеш пароля администратора, base64.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Соль пароля администратора, base64.</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>Секретный префикс пути в виде "/{seg1}/{seg2}" (без завершающего слеша).</summary>
    public string SecretPrefix => $"/{SecretPathSegment1}/{SecretPathSegment2}";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SecretPathSegment1) &&
        !string.IsNullOrWhiteSpace(SecretPathSegment2) &&
        !string.IsNullOrWhiteSpace(PasswordHash) &&
        !string.IsNullOrWhiteSpace(PasswordSalt);
}
